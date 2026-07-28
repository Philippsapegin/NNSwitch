using System.Collections.Specialized;
using System.Runtime.InteropServices;
using INSwitch.Interop;
using INSwitch.Models;

namespace INSwitch.Services;

internal enum TextSwitchMode
{
    SelectedText,
    LastWord,
    ActiveField
}

internal sealed class TextSwitchService
{
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
            var source = KeyboardLayoutService.GetCurrent(layouts);
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

            snapshot = ClipboardSnapshot.Capture();
            var marker = $"INSWITCH:{Guid.NewGuid():N}";
            if (!TrySetClipboardText(marker))
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
                    NativeMethods.SendChord(
                        NativeMethods.VkControl,
                        NativeMethods.VkShift,
                        NativeMethods.VkLeft);
                    await Task.Delay(25);
                    NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkX);
                    break;

                case TextSwitchMode.ActiveField:
                    NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkA);
                    await Task.Delay(25);
                    NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkC);
                    break;
            }

            var originalText = await WaitForCopiedTextAsync(marker);
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
            if (!TrySetClipboardText(convertedText))
            {
                NotifyFailure(showFailure, "The clipboard is currently busy.");
                return false;
            }

            KeyboardLayoutService.ActivateForForegroundWindow(target);
            await Task.Delay(35);

            if (NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                NotifyFailure(showFailure, "The active window changed. Converted text was not pasted.");
                return false;
            }

            NativeMethods.SendChord(NativeMethods.VkControl, NativeMethods.VkV);
            await Task.Delay(100);
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
            snapshot?.Restore();
            snapshot?.Dispose();
            _busy = false;
        }
    }

    private static async Task WaitForModifierKeysAsync()
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (!NativeMethods.IsKeyDown(NativeMethods.VkControl) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkShift) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkMenu) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkLwin) &&
                !NativeMethods.IsKeyDown(NativeMethods.VkRwin))
            {
                return;
            }

            await Task.Delay(10);
        }
    }

    private static async Task<string?> WaitForCopiedTextAsync(string marker)
    {
        for (var attempt = 0; attempt < 25; attempt++)
        {
            await Task.Delay(20);

            try
            {
                if (!Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    continue;
                }

                var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                if (!text.Equals(marker, StringComparison.Ordinal))
                {
                    return text;
                }
            }
            catch (ExternalException)
            {
                // Another process briefly owns the clipboard; retry.
            }
        }

        return null;
    }

    private static bool TrySetClipboardText(string text)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(15);
            }
        }

        return false;
    }

    private void NotifyFailure(bool showFailure, string message)
    {
        if (showFailure)
        {
            _notify("NN Switch", message);
        }
    }
}

internal sealed class ClipboardSnapshot : IDisposable
{
    private readonly DataObject? _data;
    private readonly List<IDisposable> _ownedData;
    private bool _disposed;

    private ClipboardSnapshot(DataObject? data, List<IDisposable> ownedData)
    {
        _data = data;
        _ownedData = ownedData;
    }

    internal static ClipboardSnapshot Capture()
    {
        var snapshot = new DataObject();
        var copiedFormats = 0;
        var ownedData = new List<IDisposable>();

        try
        {
            var current = Clipboard.GetDataObject();
            if (current is null)
            {
                return new ClipboardSnapshot(null, ownedData);
            }

            foreach (var format in current.GetFormats(autoConvert: false))
            {
                try
                {
                    var data = CloneClipboardData(current.GetData(format, autoConvert: false), ownedData);
                    if (data is null)
                    {
                        continue;
                    }

                    snapshot.SetData(format, autoConvert: false, data);
                    copiedFormats++;
                }
                catch
                {
                    // Unsupported clipboard formats are skipped.
                }
            }
        }
        catch (ExternalException)
        {
            return new ClipboardSnapshot(null, ownedData);
        }

        return new ClipboardSnapshot(copiedFormats > 0 ? snapshot : null, ownedData);
    }

    internal void Restore()
    {
        if (_data is null)
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Clipboard.SetDataObject(_data, copy: true);
                return;
            }
            catch (ExternalException)
            {
                Thread.Sleep(15);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var item in _ownedData)
        {
            item.Dispose();
        }
    }

    private static object? CloneClipboardData(object? data, ICollection<IDisposable> ownedData)
    {
        switch (data)
        {
            case null:
                return null;
            case Bitmap bitmap:
            {
                var clone = new Bitmap(bitmap);
                ownedData.Add(clone);
                return clone;
            }
            case MemoryStream stream:
            {
                var clone = new MemoryStream(stream.ToArray());
                ownedData.Add(clone);
                return clone;
            }
            case byte[] bytes:
                return bytes.ToArray();
            case string[] strings:
                return strings.ToArray();
            case StringCollection files:
            {
                var clone = new StringCollection();
                clone.AddRange(files.Cast<string>().ToArray());
                return clone;
            }
            default:
                return data;
        }
    }
}
