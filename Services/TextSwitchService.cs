using INSwitch.Interop;
using INSwitch.Models;

namespace INSwitch.Services;

internal sealed class TextSwitchService
{
    private const int ModifierReleaseAttempts = 50;
    private const int ModifierReleaseRetryDelayMs = 10;
    private const int SelectionDelayMs = 25;
    private const int LayoutActivationDelayMs = 35;
    private const int PasteCompletionDelayMs = 100;
    private const int InitialLastTokenProbeSize = 64;
    private const int MaximumLastTokenProbeSize = 4096;

    private readonly Func<AppSettings> _getSettings;
    private readonly Func<IReadOnlyList<KeyboardLayoutDescriptor>> _getLayouts;
    private readonly Action<string, string> _notify;
    private bool _busy;

    internal TextSwitchService(
        Func<AppSettings> getSettings,
        Func<IReadOnlyList<KeyboardLayoutDescriptor>> getLayouts,
        Action<string, string> notify)
    {
        _getSettings = getSettings;
        _getLayouts = getLayouts;
        _notify = notify;
    }

    internal async Task<bool> SwitchAsync(
        TextSwitchMode mode,
        bool showFailure = true,
        string? targetLayoutId = null)
    {
        if (_busy)
        {
            return false;
        }

        _busy = true;
        ClipboardSnapshot? snapshot = null;

        try
        {
            var foregroundWindow = NativeMethods.GetForegroundWindow();
            var layouts = _getLayouts();
            var source = KeyboardLayoutService.GetForWindow(foregroundWindow, layouts);
            if (foregroundWindow == IntPtr.Zero || source is null)
            {
                NotifyFailure(showFailure, "No active text field was found.");
                return false;
            }

            var target = string.IsNullOrWhiteSpace(targetLayoutId)
                ? KeyboardLayoutService.GetTarget(_getSettings(), source, layouts)
                : layouts.FirstOrDefault(layout =>
                    layout.Id.Equals(targetLayoutId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                NotifyFailure(
                    showFailure,
                    $"No target layout is configured for {source.DisplayName}.");
                return false;
            }

            await WaitForModifierKeysAsync();
            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(showFailure, "The active window changed. Try the command again.");
                return false;
            }

            snapshot = await ClipboardService.TryCaptureAsync();
            if (snapshot is null)
            {
                NotifyFailure(showFailure, "The clipboard is currently busy.");
                return false;
            }

            var marker = $"INSWITCH:{Guid.NewGuid():N}";
            if (!await ClipboardService.TrySetTextAsync(marker))
            {
                NotifyFailure(showFailure, "The clipboard is currently busy.");
                return false;
            }

            switch (mode)
            {
                case TextSwitchMode.SelectedText:
                    NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkC);
                    break;

                case TextSwitchMode.LastWord:
                    var lastToken = await SelectLastTokenAsync(marker, foregroundWindow);
                    if (lastToken is null)
                    {
                        NotifyFailure(showFailure, "No text was available at the caret.");
                        return false;
                    }

                    if (NativeMethods.GetForegroundWindow() != foregroundWindow)
                    {
                        NotifyFailure(showFailure, "The active window changed. Try the command again.");
                        return false;
                    }

                    if (!await ClipboardService.TrySetTextAsync(marker))
                    {
                        NotifyFailure(showFailure, "The clipboard is currently busy.");
                        return false;
                    }

                    await Task.Delay(SelectionDelayMs);
                    NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkX);
                    break;

                case TextSwitchMode.ActiveField:
                    NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkA);
                    await Task.Delay(SelectionDelayMs);
                    NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkC);
                    break;
            }

            var originalText = await ClipboardService.WaitForChangedTextAsync(marker);
            if (string.IsNullOrEmpty(originalText))
            {
                NotifyFailure(
                    showFailure,
                    mode == TextSwitchMode.SelectedText
                        ? "Select some text first."
                        : "No text was available at the caret.");
                return false;
            }

            var convertedText = KeyboardLayoutService.ConvertText(originalText, source, target);
            if (!await ClipboardService.TrySetTextAsync(convertedText))
            {
                NotifyFailure(showFailure, "The clipboard is currently busy.");
                return false;
            }

            KeyboardLayoutService.ActivateForForegroundWindow(target);
            await Task.Delay(LayoutActivationDelayMs);

            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(showFailure, "The active window changed. Converted text was not pasted.");
                return false;
            }

            NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkV);
            await Task.Delay(PasteCompletionDelayMs);
            return true;
        }
        catch (Exception exception)
        {
            ErrorLog.Write(exception);
            NotifyFailure(showFailure, "Text switching failed. Details were written to error.log.");
            return false;
        }
        finally
        {
            if (snapshot is not null)
            {
                try
                {
                    await snapshot.RestoreAsync();
                }
                catch (Exception exception)
                {
                    ErrorLog.Write(exception);
                }
                finally
                {
                    snapshot.Dispose();
                }
            }

            _busy = false;
        }
    }

    private static async Task<LastTokenSelection?> SelectLastTokenAsync(
        string marker,
        IntPtr foregroundWindow)
    {
        var requestedMoves = 0;
        var probeSize = InitialLastTokenProbeSize;

        while (NativeMethods.GetForegroundWindow() == foregroundWindow)
        {
            if (!await ClipboardService.TrySetTextAsync(marker))
            {
                return null;
            }

            if (NativeMethods.GetForegroundWindow() != foregroundWindow ||
                !NativeMethods.SendModifiedKeyRepeated(
                    NativeMethods.VkShift,
                    NativeMethods.VkLeft,
                    probeSize))
            {
                return null;
            }

            requestedMoves += probeSize;
            await Task.Delay(SelectionDelayMs);
            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                return null;
            }

            NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkC);
            var selectedText = await ClipboardService.WaitForChangedTextAsync(marker);
            if (string.IsNullOrEmpty(selectedText))
            {
                return null;
            }

            var selectedMoves = LastTokenSelection.CountCaretMoves(selectedText);
            var lastToken = LastTokenSelection.FromTextBeforeCaret(selectedText);
            var reachedFieldStart = selectedMoves < requestedMoves;
            var foundWhitespaceBoundary =
                lastToken is not null && lastToken.CaretMoveCount < selectedMoves;

            if (lastToken is not null && (foundWhitespaceBoundary || reachedFieldStart))
            {
                var excessSelection = selectedMoves - lastToken.CaretMoveCount;
                if (!NativeMethods.SendModifiedKeyRepeated(
                        NativeMethods.VkShift,
                        NativeMethods.VkRight,
                        excessSelection))
                {
                    return null;
                }

                return lastToken;
            }

            if (reachedFieldStart)
            {
                NativeMethods.SendModifiedKeyRepeated(
                    NativeMethods.VkShift,
                    NativeMethods.VkRight,
                    selectedMoves);
                return null;
            }

            probeSize = Math.Min(probeSize * 2, MaximumLastTokenProbeSize);
        }

        return null;
    }

    private static async Task WaitForModifierKeysAsync()
    {
        for (var attempt = 0; attempt < ModifierReleaseAttempts; attempt++)
        {
            if (!NativeMethods.IsKeyDown(NativeMethods.VkControl) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkShift) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkMenu) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkLwin) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkRwin))
            {
                return;
            }

            await Task.Delay(ModifierReleaseRetryDelayMs);
        }
    }

    private void NotifyFailure(bool showFailure, string message)
    {
        if (showFailure)
        {
            _notify("NN Switch", message);
        }
    }
}
