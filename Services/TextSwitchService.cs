using INSwitch.Interop;
using INSwitch.Models;

namespace INSwitch.Services;

internal sealed class TextSwitchService
{
    private enum TrackedSwitchResult
    {
        NotAvailable,
        Success,
        Failed
    }

    private const int ModifierReleaseAttempts = 50;
    private const int ModifierReleaseRetryDelayMs = 5;
    private const int InitialKeyboardProbeSize = 16;
    private const int MaximumKeyboardProbeSize = 16 * 1024;
    private const int FollowingTextProbeSize = 8;
    private const int FollowingTextCopyAttempts = 8;
    private const int FinalTextCopyAttempts = 16;
    private const int FinalTextCopyRetries = 2;
    private const int SelectionSettleDelayMs = 25;

    private readonly Func<AppSettings> _getSettings;
    private readonly Func<IReadOnlyList<KeyboardLayoutDescriptor>> _getLayouts;
    private readonly Action<string, string> _notify;
    private readonly TypedInputTracker _typedInputTracker;
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    internal TextSwitchService(
        Func<AppSettings> getSettings,
        Func<IReadOnlyList<KeyboardLayoutDescriptor>> getLayouts,
        Action<string, string> notify,
        TypedInputTracker typedInputTracker)
    {
        _getSettings = getSettings;
        _getLayouts = getLayouts;
        _notify = notify;
        _typedInputTracker = typedInputTracker;
    }

    internal async Task<bool> SwitchAsync(
        TextSwitchMode mode,
        bool showFailure = true,
        string? targetLayoutId = null)
    {
        await _operationGate.WaitAsync();
        ClipboardSnapshot? snapshot = null;
        var foregroundWindow = IntPtr.Zero;
        var lastWordSelectionActive = false;
        var lastWordSucceeded = false;
        LastTokenSelection? expectedLastToken = null;
        NativeMethods.EditSelectionSnapshot? originalEditSelection = null;

        try
        {
            foregroundWindow = NativeMethods.GetForegroundWindow();
            var layouts = _getLayouts();
            var detectedSource = KeyboardLayoutService.GetForWindow(foregroundWindow, layouts);
            if (foregroundWindow == IntPtr.Zero || detectedSource is null)
            {
                NotifyFailure(showFailure, "No active text field was found.");
                return false;
            }

            if (!await WaitForModifierKeysAsync())
            {
                NotifyFailure(showFailure, "Release the shortcut keys and try again.");
                return false;
            }

            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(showFailure, "The active window changed. Try the command again.");
                return false;
            }

            if (mode == TextSwitchMode.LastWord)
            {
                originalEditSelection =
                    NativeMethods.TryCaptureFocusedEditSelection(foregroundWindow);
            }

            if (mode == TextSwitchMode.LastWord)
            {
                var trackedResult = TrySwitchTrackedLastToken(
                    foregroundWindow,
                    detectedSource,
                    layouts,
                    showFailure,
                    targetLayoutId);
                if (trackedResult == TrackedSwitchResult.Success)
                {
                    lastWordSucceeded = true;
                    return true;
                }

                if (trackedResult == TrackedSwitchResult.Failed)
                {
                    return false;
                }
            }

            // From this point on the operation can inspect or mutate text that
            // was not produced by the tracked run. Never let an old in-memory
            // suffix survive a direct-edit or clipboard-based operation.
            _typedInputTracker.Reset();

            if (mode == TextSwitchMode.LastWord)
            {
                var editResult = TrySwitchFocusedEditLastToken(
                    foregroundWindow,
                    detectedSource,
                    layouts,
                    showFailure,
                    targetLayoutId);
                if (editResult == TrackedSwitchResult.Success)
                {
                    lastWordSucceeded = true;
                    return true;
                }

                if (editResult == TrackedSwitchResult.Failed)
                {
                    return false;
                }
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

            var selectionPrepared = mode switch
            {
                TextSwitchMode.SelectedText => true,
                TextSwitchMode.LastWord =>
                    (expectedLastToken = await SelectLastTokenAsync(
                        foregroundWindow,
                        marker)) is not null,
                TextSwitchMode.ActiveField => NativeMethods.SendChord(
                    NativeMethods.VkControl,
                    NativeMethods.VkA),
                _ => false
            };
            if (!selectionPrepared ||
                NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(
                    showFailure,
                    mode == TextSwitchMode.LastWord
                        ? "The last token could not be determined safely."
                        : "No text was available at the caret.");
                return false;
            }

            lastWordSelectionActive = mode == TextSwitchMode.LastWord;

            // Let the target process apply the queued selection before copying it.
            await Task.Delay(SelectionSettleDelayMs);
            var originalText = await CopySelectedTextAsync(
                foregroundWindow,
                marker,
                expectedLastToken?.Text);
            if (string.IsNullOrEmpty(originalText))
            {
                NotifyFailure(
                    showFailure,
                    mode == TextSwitchMode.SelectedText
                        ? "Select some text first."
                        : "The selected text could not be read.");
                return false;
            }

            if (mode == TextSwitchMode.LastWord &&
                !originalText.Equals(expectedLastToken!.Text, StringComparison.Ordinal))
            {
                NotifyFailure(
                    showFailure,
                    "The text selection changed before it could be replaced.");
                return false;
            }

            var source = KeyboardLayoutService.ResolveSourceForText(
                originalText,
                detectedSource,
                layouts);
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

            var convertedText = KeyboardLayoutService.ConvertText(originalText, source, target);
            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(showFailure, "The active window changed. Converted text was not inserted.");
                return false;
            }

            if (!NativeMethods.SendUnicodeText(convertedText))
            {
                NotifyFailure(showFailure, "The active application rejected the replacement text.");
                return false;
            }

            lastWordSelectionActive = false;
            lastWordSucceeded = mode == TextSwitchMode.LastWord;

            // Insert immediately after the verified copy. Dynamic web controls
            // can rerender and collapse the selection while clipboard data is
            // being restored; restoration therefore happens in finally, directly
            // after the replacement has already been queued.
            KeyboardLayoutService.ActivateForForegroundWindow(target);
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
            if (mode == TextSwitchMode.LastWord &&
                !lastWordSucceeded &&
                originalEditSelection is { } editSelection &&
                NativeMethods.TryRestoreFocusedEditSelection(
                    foregroundWindow,
                    editSelection))
            {
                lastWordSelectionActive = false;
            }

            if (lastWordSelectionActive &&
                foregroundWindow != IntPtr.Zero &&
                NativeMethods.GetForegroundWindow() == foregroundWindow)
            {
                // The verified token length is exact. Reverse the Shift+Left
                // selection with the same number of Shift+Right moves so the
                // caret returns to its original edge in browser controls too.
                NativeMethods.SendModifiedKeyRepeated(
                    NativeMethods.VkShift,
                    NativeMethods.VkRight,
                    expectedLastToken?.CaretMoveCount ?? 0);
            }

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

            _operationGate.Release();
        }
    }

    internal async Task<bool> ChangeSelectedTextCaseAsync(
        TextCaseMode mode,
        bool showFailure = true)
    {
        await _operationGate.WaitAsync();
        ClipboardSnapshot? snapshot = null;

        try
        {
            var foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                NotifyFailure(showFailure, "No active text field was found.");
                return false;
            }

            if (!await WaitForModifierKeysAsync())
            {
                NotifyFailure(showFailure, "Release the shortcut keys and try again.");
                return false;
            }

            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(showFailure, "The active window changed. Try the command again.");
                return false;
            }

            _typedInputTracker.Reset();

            var edit = NativeMethods.TryReadFocusedEdit(foregroundWindow);
            if (edit is not null)
            {
                if (edit.SelectionStart == edit.SelectionEnd)
                {
                    NotifyFailure(showFailure, "Select some text first.");
                    return false;
                }

                var originalText = edit.Text.Substring(
                    edit.SelectionStart,
                    edit.SelectionEnd - edit.SelectionStart);
                var convertedText = TextCaseConverter.Convert(originalText, mode);
                if (convertedText.Equals(originalText, StringComparison.Ordinal))
                {
                    return true;
                }

                if (!NativeMethods.TryReplaceFocusedEditRange(
                        foregroundWindow,
                        edit,
                        edit.SelectionStart,
                        edit.SelectionEnd - edit.SelectionStart,
                        convertedText))
                {
                    NotifyFailure(
                        showFailure,
                        "The active text field changed before it could be replaced.");
                    return false;
                }

                return true;
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

            var selectedText = await CopySelectedTextAsync(
                foregroundWindow,
                marker,
                expectedText: null);
            if (string.IsNullOrEmpty(selectedText))
            {
                NotifyFailure(showFailure, "Select some text first.");
                return false;
            }

            var replacement = TextCaseConverter.Convert(selectedText, mode);
            if (replacement.Equals(selectedText, StringComparison.Ordinal))
            {
                return true;
            }

            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(showFailure, "The active window changed. Converted text was not inserted.");
                return false;
            }

            if (!NativeMethods.SendUnicodeText(replacement))
            {
                NotifyFailure(showFailure, "The active application rejected the replacement text.");
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            ErrorLog.Write(exception);
            NotifyFailure(showFailure, "Case conversion failed. Details were written to error.log.");
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

            _operationGate.Release();
        }
    }

    internal async Task<bool> ActivateLayoutAsync(
        string targetLayoutId,
        bool showFailure = true)
    {
        await _operationGate.WaitAsync();

        try
        {
            var foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                NotifyFailure(showFailure, "No active window was found.");
                return false;
            }

            var target = _getLayouts().FirstOrDefault(layout =>
                layout.Id.Equals(targetLayoutId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                NotifyFailure(showFailure, "The requested keyboard layout is no longer installed.");
                return false;
            }

            if (!await WaitForModifierKeysAsync())
            {
                NotifyFailure(showFailure, "Release the shortcut keys and try again.");
                return false;
            }

            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(showFailure, "The active window changed. Try the command again.");
                return false;
            }

            _typedInputTracker.Reset();
            if (!KeyboardLayoutService.ActivateForWindow(foregroundWindow, target))
            {
                NotifyFailure(showFailure, "The active application rejected the keyboard layout change.");
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            ErrorLog.Write(exception);
            NotifyFailure(showFailure, "Keyboard layout switching failed. Details were written to error.log.");
            return false;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    internal async Task<bool> CycleLayoutAsync(bool showFailure = true)
    {
        await _operationGate.WaitAsync();

        try
        {
            var foregroundWindow = NativeMethods.GetForegroundWindow();
            if (foregroundWindow == IntPtr.Zero)
            {
                NotifyFailure(showFailure, "No active window was found.");
                return false;
            }

            var layouts = _getLayouts();
            if (layouts.Count < 2)
            {
                NotifyFailure(showFailure, "At least two keyboard layouts are required.");
                return false;
            }

            var current = KeyboardLayoutService.GetForWindow(foregroundWindow, layouts);
            var currentIndex = current is null
                ? -1
                : FindLayoutIndex(layouts, current.Id);
            var target = layouts[(currentIndex + 1) % layouts.Count];

            if (!await WaitForModifierKeysAsync())
            {
                NotifyFailure(showFailure, "Release the shortcut keys and try again.");
                return false;
            }

            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(showFailure, "The active window changed. Try the command again.");
                return false;
            }

            _typedInputTracker.Reset();
            if (!KeyboardLayoutService.ActivateForWindow(foregroundWindow, target))
            {
                NotifyFailure(showFailure, "The active application rejected the keyboard layout change.");
                return false;
            }

            return true;
        }
        catch (Exception exception)
        {
            ErrorLog.Write(exception);
            NotifyFailure(showFailure, "Keyboard layout switching failed. Details were written to error.log.");
            return false;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private static int FindLayoutIndex(
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        string layoutId)
    {
        for (var index = 0; index < layouts.Count; index++)
        {
            if (layouts[index].Id.Equals(layoutId, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private TrackedSwitchResult TrySwitchTrackedLastToken(
        IntPtr foregroundWindow,
        KeyboardLayoutDescriptor detectedSource,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        bool showFailure,
        string? targetLayoutId)
    {
        if (!_typedInputTracker.TryGetLastToken(foregroundWindow, out var trackedToken))
        {
            return TrackedSwitchResult.NotAvailable;
        }

        var trackedSource = layouts.FirstOrDefault(layout =>
                layout.Id.Equals(
                    trackedToken.SourceLayoutId,
                    StringComparison.OrdinalIgnoreCase)) ??
            detectedSource;
        var source = KeyboardLayoutService.ResolveSourceForText(
            trackedToken.Text,
            trackedSource,
            layouts);
        var target = string.IsNullOrWhiteSpace(targetLayoutId)
            ? KeyboardLayoutService.GetTarget(_getSettings(), source, layouts)
            : layouts.FirstOrDefault(layout =>
                layout.Id.Equals(targetLayoutId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return TrackedSwitchResult.NotAvailable;
        }

        var convertedText = KeyboardLayoutService.ConvertText(
            trackedToken.Text,
            source,
            target);
        if (convertedText.Equals(trackedToken.Text, StringComparison.Ordinal))
        {
            // A stale layout estimate can make the private history ambiguous.
            // Let the verified clipboard path inspect the actual field instead.
            return TrackedSwitchResult.NotAvailable;
        }

        if (NativeMethods.GetForegroundWindow() != foregroundWindow ||
            !_typedInputTracker.IsCurrent(foregroundWindow, trackedToken.Revision))
        {
            return TrackedSwitchResult.NotAvailable;
        }

        if (!NativeMethods.SendTextReplacement(
                trackedToken.CaretMoveCount,
                convertedText))
        {
            _typedInputTracker.Reset();
            NotifyFailure(
                showFailure,
                "The active application rejected the direct replacement text.");
            return TrackedSwitchResult.Failed;
        }

        _typedInputTracker.CommitReplacement(
            foregroundWindow,
            trackedToken.Revision,
            convertedText,
            target);
        KeyboardLayoutService.ActivateForForegroundWindow(target);
        return TrackedSwitchResult.Success;
    }

    private TrackedSwitchResult TrySwitchFocusedEditLastToken(
        IntPtr foregroundWindow,
        KeyboardLayoutDescriptor detectedSource,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        bool showFailure,
        string? targetLayoutId)
    {
        var edit = NativeMethods.TryReadFocusedEdit(foregroundWindow);
        if (edit is null || edit.SelectionStart != edit.SelectionEnd)
        {
            return TrackedSwitchResult.NotAvailable;
        }

        var caret = edit.SelectionEnd;
        var lastToken = LastTokenSelection.FromTextAroundCaret(
            edit.Text[..caret],
            edit.Text[caret..]);
        if (lastToken is null || lastToken.Text.Length > caret)
        {
            return TrackedSwitchResult.NotAvailable;
        }

        var source = KeyboardLayoutService.ResolveSourceForText(
            lastToken.Text,
            detectedSource,
            layouts);
        var target = string.IsNullOrWhiteSpace(targetLayoutId)
            ? KeyboardLayoutService.GetTarget(_getSettings(), source, layouts)
            : layouts.FirstOrDefault(layout =>
                layout.Id.Equals(targetLayoutId, StringComparison.OrdinalIgnoreCase));
        if (target is null)
        {
            return TrackedSwitchResult.NotAvailable;
        }

        var convertedText = KeyboardLayoutService.ConvertText(
            lastToken.Text,
            source,
            target);
        if (convertedText.Equals(lastToken.Text, StringComparison.Ordinal))
        {
            return TrackedSwitchResult.NotAvailable;
        }

        var replacementStart = caret - lastToken.Text.Length;
        if (!NativeMethods.TryReplaceFocusedEditRange(
                foregroundWindow,
                edit,
                replacementStart,
                lastToken.Text.Length,
                convertedText))
        {
            NotifyFailure(
                showFailure,
                "The active text field changed before it could be replaced.");
            return TrackedSwitchResult.Failed;
        }

        KeyboardLayoutService.ActivateForForegroundWindow(target);
        return TrackedSwitchResult.Success;
    }

    private static Task<LastTokenSelection?> SelectLastTokenAsync(
        IntPtr foregroundWindow,
        string marker) =>
        SelectLastTokenWithKeyboardProbeAsync(foregroundWindow, marker);

    private static async Task<LastTokenSelection?> SelectLastTokenWithKeyboardProbeAsync(
        IntPtr foregroundWindow,
        string marker)
    {
        var textAfterCaret = await ReadFollowingTextWithKeyboardProbeAsync(
            foregroundWindow,
            marker);
        if (textAfterCaret is null)
        {
            return null;
        }

        var selectedMoves = 0;
        var selectionMayBeActive = false;
        var keepSelection = false;
        try
        {
            for (var probeSize = InitialKeyboardProbeSize;
                 probeSize <= MaximumKeyboardProbeSize;
                 probeSize = Math.Min(probeSize * 2, MaximumKeyboardProbeSize))
            {
                var previouslySelectedMoves = selectedMoves;
                if (!await ClipboardService.TrySetTextAsync(marker))
                {
                    return null;
                }

                if (!NativeMethods.SendModifiedKeyRepeated(
                        NativeMethods.VkShift,
                        NativeMethods.VkLeft,
                        probeSize - selectedMoves))
                {
                    return null;
                }

                selectionMayBeActive = true;
                if (NativeMethods.GetForegroundWindow() != foregroundWindow)
                {
                    return null;
                }

                await Task.Delay(SelectionSettleDelayMs);
                var textBeforeCaret = await CopySelectedTextAsync(
                    foregroundWindow,
                    marker,
                    expectedText: null);
                var copiedMoves = string.IsNullOrEmpty(textBeforeCaret)
                    ? 0
                    : LastTokenSelection.CountCaretMoves(textBeforeCaret);
                if (NativeMethods.GetForegroundWindow() != foregroundWindow ||
                    string.IsNullOrEmpty(textBeforeCaret) ||
                    copiedMoves < previouslySelectedMoves)
                {
                    return null;
                }

                if (copiedMoves > probeSize)
                {
                    // Editors that copy a whole line without a selection did
                    // not apply our Shift+Left input, so there is nothing to
                    // collapse and moving the caret would be harmful.
                    selectionMayBeActive = false;
                    return null;
                }

                selectedMoves = copiedMoves;

                var lastToken = LastTokenSelection.FromTextAroundCaret(
                    textBeforeCaret,
                    textAfterCaret);
                var reachedFieldStart = selectedMoves < probeSize;
                var foundWhitespaceBoundary =
                    lastToken is not null && lastToken.CaretMoveCount < selectedMoves;
                if (lastToken is not null && (foundWhitespaceBoundary || reachedFieldStart))
                {
                    if (!NativeMethods.SendModifiedKeyRepeated(
                            NativeMethods.VkShift,
                            NativeMethods.VkRight,
                            selectedMoves - lastToken.CaretMoveCount))
                    {
                        return null;
                    }

                    // SendInput queues the keystrokes; the foreground control may apply
                    // the resulting selection on the next message-loop iteration.
                    await Task.Delay(SelectionSettleDelayMs);
                    keepSelection = true;
                    return lastToken;
                }

                if (reachedFieldStart || probeSize == MaximumKeyboardProbeSize)
                {
                    return null;
                }
            }

            return null;
        }
        finally
        {
            if (!keepSelection &&
                selectionMayBeActive &&
                NativeMethods.GetForegroundWindow() == foregroundWindow)
            {
                // Selection grew to the left, so Right restores the original
                // caret in one input regardless of the selected length.
                NativeMethods.SendChord(NativeMethods.VkRight);
            }
        }
    }

    private static async Task<string?> ReadFollowingTextWithKeyboardProbeAsync(
        IntPtr foregroundWindow,
        string marker)
    {
        var selectedMoves = 0;
        var textAfterCaret = string.Empty;
        var selectionMayBeActive = false;

        try
        {
            for (var expectedMoves = 1;
                 expectedMoves <= FollowingTextProbeSize;
                 expectedMoves++)
            {
                if (!await ClipboardService.TrySetTextAsync(marker))
                {
                    return null;
                }

                if (!NativeMethods.SendModifiedKeyRepeated(
                        NativeMethods.VkShift,
                        NativeMethods.VkRight,
                        1))
                {
                    return null;
                }

                selectionMayBeActive = true;
                if (NativeMethods.GetForegroundWindow() != foregroundWindow)
                {
                    return null;
                }

                await Task.Delay(SelectionSettleDelayMs);
                if (!SendCopyCommand(foregroundWindow))
                {
                    return null;
                }

                var copiedText = await ClipboardService.WaitForChangedTextAsync(
                    marker,
                    FollowingTextCopyAttempts);
                if (NativeMethods.GetForegroundWindow() != foregroundWindow)
                {
                    return null;
                }

                if (copiedText is null)
                {
                    var restoredText = await RestoreFollowingSelectionAsync(
                        selectedMoves,
                        textAfterCaret);
                    selectionMayBeActive = restoredText is null;
                    return restoredText;
                }

                var copiedMoves = LastTokenSelection.CountCaretMoves(copiedText);
                if (copiedMoves > expectedMoves ||
                    copiedMoves < selectedMoves ||
                    !copiedText.StartsWith(textAfterCaret, StringComparison.Ordinal))
                {
                    // A one-step selection cannot produce more than expectedMoves.
                    // Some editors copy the current line when there is no
                    // selection; in that case the caret has not moved at all.
                    if (selectedMoves == 0 && copiedMoves > expectedMoves)
                    {
                        selectionMayBeActive = false;
                        return string.Empty;
                    }

                    return null;
                }

                if (copiedMoves == selectedMoves)
                {
                    var restoredText = await RestoreFollowingSelectionAsync(
                        selectedMoves,
                        textAfterCaret);
                    selectionMayBeActive = restoredText is null;
                    return restoredText;
                }

                if (copiedMoves != selectedMoves + 1)
                {
                    return null;
                }

                selectedMoves = copiedMoves;
                textAfterCaret = copiedText;
                if (!LastTokenSelection.IsClosingDelimiter(textAfterCaret[^1]))
                {
                    var restoredText = await RestoreFollowingSelectionAsync(
                        selectedMoves,
                        textAfterCaret);
                    selectionMayBeActive = restoredText is null;
                    return restoredText;
                }
            }

            var finalRestoredText = await RestoreFollowingSelectionAsync(
                selectedMoves,
                textAfterCaret);
            selectionMayBeActive = finalRestoredText is null;
            return finalRestoredText;
        }
        finally
        {
            if (selectionMayBeActive &&
                NativeMethods.GetForegroundWindow() == foregroundWindow)
            {
                // Selection grew to the right, so Left restores the original
                // caret even if clipboard reading failed unexpectedly.
                NativeMethods.SendChord(NativeMethods.VkLeft);
            }
        }
    }

    private static async Task<string?> RestoreFollowingSelectionAsync(
        int selectedMoves,
        string textAfterCaret)
    {
        if (!NativeMethods.SendModifiedKeyRepeated(
                NativeMethods.VkShift,
                NativeMethods.VkLeft,
                selectedMoves))
        {
            return null;
        }

        if (selectedMoves > 0)
        {
            await Task.Delay(SelectionSettleDelayMs);
        }

        return textAfterCaret;
    }

    private static async Task<bool> WaitForModifierKeysAsync()
    {
        for (var attempt = 0; attempt < ModifierReleaseAttempts; attempt++)
        {
            if (!NativeMethods.IsKeyDown(NativeMethods.VkControl) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkShift) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkMenu) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkLwin) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkRwin))
            {
                return true;
            }

            await Task.Delay(ModifierReleaseRetryDelayMs);
        }

        return false;
    }

    private static async Task<string?> CopySelectedTextAsync(
        IntPtr foregroundWindow,
        string marker,
        string? expectedText)
    {
        for (var attempt = 0; attempt < FinalTextCopyRetries; attempt++)
        {
            if (!await ClipboardService.TrySetTextAsync(marker) ||
                !SendCopyCommand(foregroundWindow))
            {
                continue;
            }

            var text = await ClipboardService.WaitForChangedTextAsync(
                marker,
                FinalTextCopyAttempts);
            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                return null;
            }

            if (!string.IsNullOrEmpty(text) &&
                (expectedText is null ||
                 text.Equals(expectedText, StringComparison.Ordinal)))
            {
                return text;
            }

            await Task.Delay(SelectionSettleDelayMs);
        }

        return null;
    }

    private static bool SendCopyCommand(IntPtr foregroundWindow) =>
        NativeMethods.GetForegroundWindow() == foregroundWindow &&
        NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkC);

    private void NotifyFailure(bool showFailure, string message)
    {
        if (showFailure)
        {
            _notify("NN Switch", message);
        }
    }
}
