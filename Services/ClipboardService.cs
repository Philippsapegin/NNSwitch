using System.Runtime.InteropServices;

namespace INSwitch.Services;

internal static class ClipboardService
{
    private const int ClipboardWriteAttempts = 10;
    private const int ClipboardWriteRetryDelayMs = 15;
    private const int ClipboardReadAttempts = 25;
    private const int ClipboardReadRetryDelayMs = 20;

    internal static async Task<ClipboardSnapshot?> TryCaptureAsync()
    {
        for (var attempt = 0; attempt < ClipboardWriteAttempts; attempt++)
        {
            if (ClipboardSnapshot.TryCapture(out var snapshot))
            {
                return snapshot;
            }

            if (attempt + 1 < ClipboardWriteAttempts)
            {
                await Task.Delay(ClipboardWriteRetryDelayMs);
            }
        }

        return null;
    }

    internal static async Task<bool> TrySetTextAsync(string text)
    {
        for (var attempt = 0; attempt < ClipboardWriteAttempts; attempt++)
        {
            try
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            }
            catch (ExternalException) when (attempt + 1 < ClipboardWriteAttempts)
            {
                await Task.Delay(ClipboardWriteRetryDelayMs);
            }
            catch (ExternalException)
            {
                return false;
            }
        }

        return false;
    }

    internal static async Task<string?> WaitForChangedTextAsync(string marker)
    {
        for (var attempt = 0; attempt < ClipboardReadAttempts; attempt++)
        {
            await Task.Delay(ClipboardReadRetryDelayMs);

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
}
