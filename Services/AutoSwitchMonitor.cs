using System.Runtime.InteropServices;
using System.Text;
using INSwitch.Interop;
using INSwitch.Models;

namespace INSwitch.Services;

internal sealed class AutoSwitchMonitor : IDisposable
{
    private readonly Func<AppSettings> _getSettings;
    private readonly Func<IReadOnlyList<KeyboardLayoutDescriptor>> _getLayouts;
    private readonly Func<Task> _switchLastWord;
    private readonly NativeMethods.LowLevelKeyboardProc _hookCallback;
    private readonly System.Windows.Forms.Timer _decisionTimer;
    private readonly StringBuilder _currentWord = new();
    private IntPtr _hookHandle;
    private IntPtr _wordWindow;
    private string _wordLayoutId = string.Empty;
    private AutoSwitchCandidate? _pendingCandidate;
    private bool _disposed;

    internal AutoSwitchMonitor(
        Func<AppSettings> getSettings,
        Func<IReadOnlyList<KeyboardLayoutDescriptor>> getLayouts,
        Func<Task> switchLastWord)
    {
        _getSettings = getSettings;
        _getLayouts = getLayouts;
        _switchLastWord = switchLastWord;
        _hookCallback = HookCallback;
        _decisionTimer = new System.Windows.Forms.Timer { Interval = 55 };
        _decisionTimer.Tick += DecisionTimerOnTick;
    }

    internal bool Start()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            return true;
        }

        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _hookCallback,
            IntPtr.Zero,
            0);

        return _hookHandle != IntPtr.Zero;
    }

    internal void Stop()
    {
        _decisionTimer.Stop();
        _pendingCandidate = null;
        ResetWord();

        if (_hookHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _decisionTimer.Dispose();
        GC.SuppressFinalize(this);
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 &&
            (wParam.ToInt32() == NativeMethods.WmKeyDown ||
             wParam.ToInt32() == NativeMethods.WmSysKeyDown))
        {
            var data = Marshal.PtrToStructure<NativeMethods.Kbdllhookstruct>(lParam);
            if ((data.Flags & NativeMethods.LlkhfInjected) == 0)
            {
                ProcessKey(data);
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private void ProcessKey(NativeMethods.Kbdllhookstruct data)
    {
        var virtualKey = (ushort)data.VkCode;

        if (virtualKey is NativeMethods.VkShift or NativeMethods.VkControl or
            NativeMethods.VkMenu or NativeMethods.VkLwin or NativeMethods.VkRwin)
        {
            return;
        }

        if (NativeMethods.IsKeyDown(NativeMethods.VkControl) ||
            NativeMethods.IsKeyDown(NativeMethods.VkMenu) ||
            NativeMethods.IsKeyDown(NativeMethods.VkLwin) ||
            NativeMethods.IsKeyDown(NativeMethods.VkRwin))
        {
            ResetWord();
            return;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var layouts = _getLayouts();
        var source = KeyboardLayoutService.GetCurrent(layouts);
        if (foregroundWindow == IntPtr.Zero || source is null)
        {
            ResetWord();
            return;
        }

        if (_wordWindow != IntPtr.Zero &&
            (_wordWindow != foregroundWindow ||
             !_wordLayoutId.Equals(source.Id, StringComparison.OrdinalIgnoreCase)))
        {
            ResetWord();
        }

        _wordWindow = foregroundWindow;
        _wordLayoutId = source.Id;

        if (virtualKey == NativeMethods.VkBack)
        {
            if (_currentWord.Length > 0)
            {
                _currentWord.Length--;
            }

            return;
        }

        if (virtualKey is NativeMethods.VkSpace or NativeMethods.VkReturn or NativeMethods.VkTab)
        {
            QueueDecision(source, foregroundWindow);
            ResetWord(keepPendingDecision: true);
            return;
        }

        if (virtualKey is NativeMethods.VkLeft or NativeMethods.VkRight or
            NativeMethods.VkUp or NativeMethods.VkDown or NativeMethods.VkDelete or
            NativeMethods.VkEscape)
        {
            ResetWord();
            return;
        }

        var translated = KeyboardLayoutService.TranslateKey(
            data.VkCode,
            data.ScanCode,
            source.Handle);

        foreach (var character in translated)
        {
            if (char.IsLetter(character) || character is '\'' or '’' or '-')
            {
                _currentWord.Append(character);
            }
        }

        if (_currentWord.Length > 64)
        {
            ResetWord();
        }
    }

    private void QueueDecision(KeyboardLayoutDescriptor source, IntPtr foregroundWindow)
    {
        if (_currentWord.Length == 0 || !_getSettings().AutoSwitch)
        {
            return;
        }

        var target = KeyboardLayoutService.GetTarget(_getSettings(), source, _getLayouts());
        if (target is null)
        {
            return;
        }

        var sourceWord = _currentWord.ToString();
        var convertedWord = KeyboardLayoutService.ConvertText(sourceWord, source, target);
        if (!LanguageHeuristics.ShouldSwitch(sourceWord, convertedWord, source, target))
        {
            return;
        }

        _pendingCandidate = new AutoSwitchCandidate(foregroundWindow, source.Id);
        _decisionTimer.Stop();
        _decisionTimer.Start();
    }

    private async void DecisionTimerOnTick(object? sender, EventArgs eventArgs)
    {
        _decisionTimer.Stop();
        var candidate = _pendingCandidate;
        _pendingCandidate = null;

        if (candidate is null ||
            NativeMethods.GetForegroundWindow() != candidate.Window ||
            !_getSettings().AutoSwitch)
        {
            return;
        }

        try
        {
            await _switchLastWord();
        }
        catch (Exception exception)
        {
            ErrorLog.Write(exception);
        }
    }

    private void ResetWord(bool keepPendingDecision = false)
    {
        _currentWord.Clear();
        _wordWindow = IntPtr.Zero;
        _wordLayoutId = string.Empty;

        if (!keepPendingDecision)
        {
            _decisionTimer.Stop();
            _pendingCandidate = null;
        }
    }

    private sealed record AutoSwitchCandidate(IntPtr Window, string LayoutId);
}
