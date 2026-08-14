using System.Globalization;
using INSwitch.Interop;

namespace INSwitch.Services;

internal sealed record TrackedLastToken(
    string Text,
    int CaretMoveCount,
    string SourceLayoutId,
    long Revision);

internal sealed class TypedInputTracker
{
    private const int MaximumTrackedUnits = 4096;
    private const long MaximumTrackingAgeMs = 30_000;

    private readonly Func<IReadOnlyList<KeyboardLayoutDescriptor>> _getLayouts;
    private readonly object _sync = new();
    private readonly List<string> _units = new();
    private readonly System.Threading.Timer _expirationTimer;
    private IntPtr _foregroundWindow;
    private string? _sourceLayoutId;
    private long _lastInputTick;
    private long _revision;
    private bool _pointerHookActive;

    internal TypedInputTracker(
        Func<IReadOnlyList<KeyboardLayoutDescriptor>> getLayouts)
    {
        _getLayouts = getLayouts;
        _expirationTimer = new System.Threading.Timer(
            _ => ExpireInactiveHistory(),
            null,
            Timeout.Infinite,
            Timeout.Infinite);
    }

    internal void SetPointerHookActive(bool active)
    {
        lock (_sync)
        {
            _pointerHookActive = active;
            ResetCore();
        }
    }

    internal void ObservePointerInput()
    {
        lock (_sync)
        {
            ResetCore();
        }
    }

    internal void ObserveKeyboardInput(
        int message,
        NativeMethods.Kbdllhookstruct keyboardData)
    {
        if (message is not NativeMethods.WmKeydown and not NativeMethods.WmSyskeydown)
        {
            return;
        }

        var virtualKey = keyboardData.VirtualKey;
        if (IsModifierKey(virtualKey) || virtualKey == NativeMethods.VkCapital)
        {
            return;
        }

        lock (_sync)
        {
            if (HasCommandModifier())
            {
                ResetCore();
                return;
            }

            var foregroundWindow = NativeMethods.GetForegroundWindow();
            var source = KeyboardLayoutService.GetForWindow(
                foregroundWindow,
                _getLayouts());
            if (foregroundWindow == IntPtr.Zero || source is null)
            {
                ResetCore();
                return;
            }

            if (virtualKey == NativeMethods.VkBack)
            {
                ObserveBackspace(foregroundWindow, source);
                return;
            }

            if (IsEditingOrNavigationKey(virtualKey))
            {
                ResetCore();
                return;
            }

            var shift = NativeMethods.IsKeyDown(NativeMethods.VkShift);
            var capsLock = NativeMethods.IsKeyToggled(NativeMethods.VkCapital);
            var numLock = NativeMethods.IsKeyToggled(NativeMethods.VkNumlock);
            var text = KeyboardLayoutService.TryTranslatePhysicalKey(
                virtualKey,
                keyboardData.ScanCode,
                source,
                shift,
                capsLock,
                numLock);
            if (string.IsNullOrEmpty(text) ||
                text.Any(char.IsControl) ||
                LastTokenSelection.CountCaretMoves(text) != 1)
            {
                ResetCore();
                return;
            }

            StartOrValidateRun(foregroundWindow, source);
            if (_units.Count >= MaximumTrackedUnits)
            {
                ResetCore();
                return;
            }

            _units.Add(text);
            _lastInputTick = Environment.TickCount64;
            _revision++;
            ScheduleExpirationCore(MaximumTrackingAgeMs);
        }
    }

    internal bool TryGetLastToken(
        IntPtr foregroundWindow,
        out TrackedLastToken token)
    {
        lock (_sync)
        {
            token = null!;
            if (!_pointerHookActive ||
                foregroundWindow == IntPtr.Zero ||
                foregroundWindow != _foregroundWindow ||
                _units.Count == 0 ||
                string.IsNullOrWhiteSpace(_sourceLayoutId) ||
                Environment.TickCount64 - _lastInputTick > MaximumTrackingAgeMs)
            {
                return false;
            }

            var history = string.Concat(_units);
            var selection = LastTokenSelection.FromTextBeforeCaret(history);
            if (selection is null ||
                selection.CaretMoveCount <= 0 ||
                selection.CaretMoveCount > _units.Count ||
                LastTokenSelection.IsOpeningDelimiter(selection.Text[0]))
            {
                return false;
            }

            token = new TrackedLastToken(
                selection.Text,
                selection.CaretMoveCount,
                _sourceLayoutId,
                _revision);
            return true;
        }
    }

    internal bool IsCurrent(IntPtr foregroundWindow, long revision)
    {
        lock (_sync)
        {
            return _pointerHookActive &&
                   foregroundWindow != IntPtr.Zero &&
                   foregroundWindow == _foregroundWindow &&
                   revision == _revision;
        }
    }

    internal void CommitReplacement(
        IntPtr foregroundWindow,
        long expectedRevision,
        string replacement,
        KeyboardLayoutDescriptor sourceLayout)
    {
        lock (_sync)
        {
            if (foregroundWindow != _foregroundWindow ||
                expectedRevision != _revision)
            {
                ResetCore();
                return;
            }

            _units.Clear();
            var enumerator = StringInfo.GetTextElementEnumerator(replacement);
            while (enumerator.MoveNext())
            {
                if (_units.Count >= MaximumTrackedUnits)
                {
                    ResetCore();
                    return;
                }

                _units.Add(enumerator.GetTextElement());
            }

            _sourceLayoutId = sourceLayout.Id;
            _lastInputTick = Environment.TickCount64;
            _revision++;
            ScheduleExpirationCore(MaximumTrackingAgeMs);
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            ResetCore();
        }
    }

    private void ObserveBackspace(
        IntPtr foregroundWindow,
        KeyboardLayoutDescriptor source)
    {
        if (!IsSameRun(foregroundWindow, source) || _units.Count == 0)
        {
            ResetCore();
            return;
        }

        _units.RemoveAt(_units.Count - 1);
        if (_units.Count == 0)
        {
            ResetCore();
            return;
        }

        _lastInputTick = Environment.TickCount64;
        _revision++;
        ScheduleExpirationCore(MaximumTrackingAgeMs);
    }

    private void StartOrValidateRun(
        IntPtr foregroundWindow,
        KeyboardLayoutDescriptor source)
    {
        if (!IsSameRun(foregroundWindow, source) ||
            Environment.TickCount64 - _lastInputTick > MaximumTrackingAgeMs)
        {
            ResetCore();
            _foregroundWindow = foregroundWindow;
            _sourceLayoutId = source.Id;
        }
    }

    private bool IsSameRun(
        IntPtr foregroundWindow,
        KeyboardLayoutDescriptor source) =>
        foregroundWindow == _foregroundWindow &&
        source.Id.Equals(_sourceLayoutId, StringComparison.OrdinalIgnoreCase);

    private void ResetCore()
    {
        _expirationTimer.Change(Timeout.Infinite, Timeout.Infinite);
        _units.Clear();
        _foregroundWindow = IntPtr.Zero;
        _sourceLayoutId = null;
        _lastInputTick = 0;
        _revision++;
    }

    private void ExpireInactiveHistory()
    {
        lock (_sync)
        {
            if (_units.Count == 0)
            {
                return;
            }

            var elapsed = Environment.TickCount64 - _lastInputTick;
            var remaining = MaximumTrackingAgeMs - elapsed;
            if (remaining <= 0)
            {
                ResetCore();
                return;
            }

            ScheduleExpirationCore(remaining);
        }
    }

    private void ScheduleExpirationCore(long delayMilliseconds) =>
        _expirationTimer.Change(
            checked((int)Math.Clamp(delayMilliseconds, 1, int.MaxValue)),
            Timeout.Infinite);

    private static bool HasCommandModifier() =>
        NativeMethods.IsKeyDown(NativeMethods.VkControl) ||
        NativeMethods.IsKeyDown(NativeMethods.VkMenu) ||
        NativeMethods.IsKeyDown(NativeMethods.VkLwin) ||
        NativeMethods.IsKeyDown(NativeMethods.VkRwin);

    private static bool IsModifierKey(uint virtualKey) =>
        virtualKey is NativeMethods.VkShift or
            NativeMethods.VkControl or
            NativeMethods.VkMenu or
            NativeMethods.VkLshift or
            NativeMethods.VkRshift or
            NativeMethods.VkLcontrol or
            NativeMethods.VkRcontrol or
            NativeMethods.VkLmenu or
            NativeMethods.VkRmenu or
            NativeMethods.VkLwin or
            NativeMethods.VkRwin;

    private static bool IsEditingOrNavigationKey(uint virtualKey) =>
        virtualKey is NativeMethods.VkTab or
            NativeMethods.VkReturn or
            NativeMethods.VkEscape or
            NativeMethods.VkPrior or
            NativeMethods.VkNext or
            NativeMethods.VkEnd or
            NativeMethods.VkHome or
            NativeMethods.VkLeft or
            NativeMethods.VkUp or
            NativeMethods.VkRight or
            NativeMethods.VkDown or
            NativeMethods.VkInsert or
            NativeMethods.VkDelete;
}
