using System.Globalization;
using System.Text;
using INSwitch.Interop;
using INSwitch.Models;

namespace INSwitch.Services;

internal sealed record KeyboardLayoutDescriptor(
    string Id,
    string DisplayName,
    string TwoLetterLanguage,
    IntPtr Handle);

internal static class KeyboardLayoutService
{
    internal static IReadOnlyList<KeyboardLayoutDescriptor> GetInstalled()
    {
        var layouts = new List<KeyboardLayoutDescriptor>();
        var knownIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (InputLanguage inputLanguage in InputLanguage.InstalledInputLanguages)
        {
            var id = GetId(inputLanguage.Handle);
            if (!knownIds.Add(id))
            {
                continue;
            }

            var culture = inputLanguage.Culture;
            var language = culture.TwoLetterISOLanguageName.ToLowerInvariant();
            var name = $"{inputLanguage.LayoutName} — {culture.EnglishName}";
            layouts.Add(new KeyboardLayoutDescriptor(id, name, language, inputLanguage.Handle));
        }

        return layouts
            .OrderBy(layout => layout.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    internal static KeyboardLayoutDescriptor? GetCurrent(IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        return GetForWindow(foregroundWindow, layouts);
    }

    internal static KeyboardLayoutDescriptor? GetForWindow(
        IntPtr windowHandle,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return null;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(windowHandle, IntPtr.Zero);
        var handle = NativeMethods.GetKeyboardLayout(threadId);
        var id = GetId(handle);
        return layouts.FirstOrDefault(layout => layout.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    }

    internal static KeyboardLayoutDescriptor? GetTarget(
        AppSettings settings,
        KeyboardLayoutDescriptor source,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        if (!settings.SwitchTargets.TryGetValue(source.Id, out var targetId) ||
            string.IsNullOrWhiteSpace(targetId))
        {
            return null;
        }

        return layouts.FirstOrDefault(layout =>
            layout.Id.Equals(targetId, StringComparison.OrdinalIgnoreCase));
    }

    internal static bool ActivateForForegroundWindow(KeyboardLayoutDescriptor target)
    {
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        return foregroundWindow != IntPtr.Zero &&
               NativeMethods.PostMessage(
                   foregroundWindow,
                   NativeMethods.WmInputLanguageChangeRequest,
                   IntPtr.Zero,
                   target.Handle);
    }

    internal static string ConvertText(
        string text,
        KeyboardLayoutDescriptor source,
        KeyboardLayoutDescriptor target)
    {
        var result = new StringBuilder(text.Length);

        foreach (var character in text)
        {
            if (char.IsControl(character))
            {
                result.Append(character);
                continue;
            }

            var sourceKey = NativeMethods.VkKeyScanEx(character, source.Handle);
            if (sourceKey == -1)
            {
                result.Append(character);
                continue;
            }

            var virtualKey = (uint)(sourceKey & 0xFF);
            var shiftState = (sourceKey >> 8) & 0xFF;
            var keyboardState = CreateKeyboardState(shiftState);
            var scanCode = NativeMethods.MapVirtualKeyEx(
                virtualKey,
                NativeMethods.MapvkVkToVsc,
                target.Handle);
            var buffer = new StringBuilder(8);
            var translatedLength = NativeMethods.ToUnicodeEx(
                virtualKey,
                scanCode,
                keyboardState,
                buffer,
                buffer.Capacity,
                0,
                target.Handle);

            if (translatedLength > 0)
            {
                result.Append(buffer.ToString(0, translatedLength));
            }
            else
            {
                if (translatedLength < 0)
                {
                    ClearDeadKey(virtualKey, scanCode, target.Handle);
                }

                result.Append(character);
            }
        }

        return result.ToString();
    }

    internal static string GetId(IntPtr handle) =>
        unchecked((uint)handle.ToInt64()).ToString("X8", CultureInfo.InvariantCulture);

    private static byte[] CreateKeyboardState(int shiftState)
    {
        var keyboardState = new byte[256];

        if ((shiftState & 1) != 0)
        {
            keyboardState[NativeMethods.VkShift] = 0x80;
        }

        if ((shiftState & 2) != 0)
        {
            keyboardState[NativeMethods.VkControl] = 0x80;
        }

        if ((shiftState & 4) != 0)
        {
            keyboardState[NativeMethods.VkMenu] = 0x80;
        }

        return keyboardState;
    }

    private static void ClearDeadKey(uint virtualKey, uint scanCode, IntPtr layout)
    {
        var state = new byte[256];
        var buffer = new StringBuilder(8);
        NativeMethods.ToUnicodeEx(virtualKey, scanCode, state, buffer, buffer.Capacity, 0, layout);
    }
}
