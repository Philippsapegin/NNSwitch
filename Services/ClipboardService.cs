using System.Runtime.InteropServices;

namespace INSwitch.Services;

internal static class ClipboardService
{
    private const int ClipboardWriteAttempts = 30;
    private const int ClipboardWriteRetryDelayMs = 5;
    private const int ClipboardReadAttempts = 100;
    private const int ClipboardReadRetryDelayMs = 5;

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

    internal static async Task<string?> WaitForChangedTextAsync(
        string marker,
        int maximumAttempts = ClipboardReadAttempts)
    {
        maximumAttempts = Math.Clamp(maximumAttempts, 1, ClipboardReadAttempts);
        for (var attempt = 0; attempt < maximumAttempts; attempt++)
        {
            try
            {
                if (Clipboard.ContainsText(TextDataFormat.UnicodeText))
                {
                    var text = Clipboard.GetText(TextDataFormat.UnicodeText);
                    if (!text.Equals(marker, StringComparison.Ordinal))
                    {
                        return text;
                    }
                }
            }
            catch (ExternalException)
            {
                // Another process briefly owns the clipboard; retry.
            }

            if (attempt + 1 < maximumAttempts)
            {
                await Task.Delay(ClipboardReadRetryDelayMs);
            }
        }

        return null;
    }
}
