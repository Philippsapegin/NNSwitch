using System.Runtime.InteropServices;
using System.Text;
using INSwitch.Models;

namespace INSwitch.Interop;

internal static class NativeMethods
{
    private const int SendInputRetryAttempts = 10;
    private const int SendInputRetryDelayMs = 1;
    private const uint WmGettext = 0x000D;
    private const uint WmGettextlength = 0x000E;
    private const uint EmGetSel = 0x00B0;
    private const uint EmSetSel = 0x00B1;
    internal const int EmReplacesel = 0x00C2;
    private const uint SmtoAbortIfHung = 0x0002;
    private const uint SelectionMessageTimeoutMs = 50;
    private const uint EditMessageTimeoutMs = 250;
    private const int MaximumEditTextLength = 1024 * 1024;

    internal static readonly IntPtr InjectedInputMarker = new(0x4E4E5357);

    internal const int WmHotkey = 0x0312;
    internal const int WmAppExclusiveHotkey = 0x8001;
    internal const int WmInputLanguageChangeRequest = 0x0050;
    internal const int WmSetredraw = 0x000B;
    internal const int WmKeydown = 0x0100;
    internal const int WmKeyup = 0x0101;
    internal const int WmSyskeydown = 0x0104;
    internal const int WmSyskeyup = 0x0105;
    internal const int WmLbuttondown = 0x0201;
    internal const int WmRbuttondown = 0x0204;
    internal const int WmMbuttondown = 0x0207;
    internal const int WmMousewheel = 0x020A;
    internal const int WmXbuttondown = 0x020B;
    internal const int WmMousehwheel = 0x020E;

    internal const int WhKeyboardLl = 13;
    internal const int WhMouseLl = 14;

    internal const ushort VkBack = 0x08;
    internal const ushort VkTab = 0x09;
    internal const ushort VkReturn = 0x0D;
    internal const ushort VkShift = 0x10;
    internal const ushort VkControl = 0x11;
    internal const ushort VkMenu = 0x12;
    internal const ushort VkPause = 0x13;
    internal const ushort VkCapital = 0x14;
    internal const ushort VkEscape = 0x1B;
    internal const ushort VkPrior = 0x21;
    internal const ushort VkNext = 0x22;
    internal const ushort VkEnd = 0x23;
    internal const ushort VkHome = 0x24;
    internal const ushort VkLeft = 0x25;
    internal const ushort VkUp = 0x26;
    internal const ushort VkRight = 0x27;
    internal const ushort VkDown = 0x28;
    internal const ushort VkInsert = 0x2D;
    internal const ushort VkDelete = 0x2E;
    internal const ushort VkLshift = 0xA0;
    internal const ushort VkRshift = 0xA1;
    internal const ushort VkLcontrol = 0xA2;
    internal const ushort VkRcontrol = 0xA3;
    internal const ushort VkLmenu = 0xA4;
    internal const ushort VkRmenu = 0xA5;
    internal const ushort VkLwin = 0x5B;
    internal const ushort VkRwin = 0x5C;
    internal const ushort VkNumlock = 0x90;
    internal const ushort VkA = 0x41;
    internal const ushort VkC = 0x43;

    internal const uint KeyeventfKeyup = 0x0002;
    internal const uint KeyeventfUnicode = 0x0004;
    internal const uint InputKeyboard = 1;
    internal const uint MapvkVkToVsc = 0;
    internal const uint TounicodeNoStateChange = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        internal uint Type;
        internal InputUnion Data;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        internal Keybdinput Keyboard;

        // INPUT uses the largest union member for its native size, including on x64.
        [FieldOffset(0)]
        internal Mouseinput Mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Keybdinput
    {
        internal ushort VirtualKey;
        internal ushort ScanCode;
        internal uint Flags;
        internal uint Time;
        internal IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Mouseinput
    {
        internal int X;
        internal int Y;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Kbdllhookstruct
    {
        internal uint VirtualKey;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Guithreadinfo
    {
        internal uint Size;
        internal uint Flags;
        internal IntPtr ActiveWindow;
        internal IntPtr FocusWindow;
        internal IntPtr CaptureWindow;
        internal IntPtr MenuOwnerWindow;
        internal IntPtr MoveSizeWindow;
        internal IntPtr CaretWindow;
        internal Rect CaretRectangle;
    }

    internal readonly record struct EditSelectionSnapshot(
        IntPtr Window,
        int Start,
        int End);

    internal sealed record FocusedEditSnapshot(
        IntPtr Window,
        string Text,
        int SelectionStart,
        int SelectionEnd);

    internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr wParam, IntPtr lParam);
    internal delegate IntPtr LowLevelMouseProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterHotKey(
        IntPtr windowHandle,
        int id,
        HotkeyModifiers modifiers,
        uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnregisterHotKey(IntPtr windowHandle, int id);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelKeyboardProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int hookId,
        LowLevelMouseProc callback,
        IntPtr moduleHandle,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hookHandle);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(
        IntPtr hookHandle,
        int code,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, IntPtr processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(
        uint threadId,
        ref Guithreadinfo info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(
        IntPtr windowHandle,
        StringBuilder className,
        int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport(
        "user32.dll",
        EntryPoint = "SendMessageTimeoutW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutBuffer(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        StringBuilder lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport(
        "user32.dll",
        EntryPoint = "SendMessageTimeoutW",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutString(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        string lParam,
        uint flags,
        uint timeout,
        out IntPtr result);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetKeyboardLayout(uint threadId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern short VkKeyScanEx(char character, IntPtr keyboardLayout);

    [DllImport("user32.dll")]
    internal static extern uint MapVirtualKeyEx(uint code, uint mapType, IntPtr keyboardLayout);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int ToUnicodeEx(
        uint virtualKey,
        uint scanCode,
        byte[] keyboardState,
        [Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder buffer,
        int bufferSize,
        uint flags,
        IntPtr keyboardLayout);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SendNotifyMessage(
        IntPtr windowHandle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InvalidateRect(
        IntPtr windowHandle,
        IntPtr rectangle,
        [MarshalAs(UnmanagedType.Bool)] bool erase);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmSetWindowAttribute(
        IntPtr windowHandle,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    internal static extern int SetWindowTheme(
        IntPtr windowHandle,
        string? subAppName,
        string? subIdList);

    internal static bool IsKeyDown(int virtualKey) => (GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    internal static bool IsKeyToggled(int virtualKey) => (GetKeyState(virtualKey) & 0x0001) != 0;

    internal static EditSelectionSnapshot? TryCaptureFocusedEditSelection(
        IntPtr foregroundWindow)
    {
        var focusWindow = TryGetFocusWindow(foregroundWindow);
        if (focusWindow == IntPtr.Zero || !IsEditControl(focusWindow))
        {
            return null;
        }

        return TryCaptureEditSelection(focusWindow);
    }

    internal static FocusedEditSnapshot? TryReadFocusedEdit(
        IntPtr foregroundWindow)
    {
        var focusWindow = TryGetFocusWindow(foregroundWindow);
        if (focusWindow == IntPtr.Zero || !IsEditControl(focusWindow))
        {
            return null;
        }

        var selection = TryCaptureEditSelection(focusWindow);
        if (selection is null ||
            SendMessageTimeout(
                focusWindow,
                WmGettextlength,
                IntPtr.Zero,
                IntPtr.Zero,
                SmtoAbortIfHung,
                EditMessageTimeoutMs,
                out var textLengthResult) == IntPtr.Zero)
        {
            return null;
        }

        var textLength = textLengthResult.ToInt64();
        if (textLength < 0 || textLength > MaximumEditTextLength)
        {
            return null;
        }

        var buffer = new StringBuilder(checked((int)textLength + 1));
        if (SendMessageTimeoutBuffer(
                focusWindow,
                WmGettext,
                new IntPtr(buffer.Capacity),
                buffer,
                SmtoAbortIfHung,
                EditMessageTimeoutMs,
                out _) == IntPtr.Zero)
        {
            return null;
        }

        var text = buffer.ToString();
        if (selection.Value.Start < 0 ||
            selection.Value.End < selection.Value.Start ||
            selection.Value.End > text.Length)
        {
            return null;
        }

        return new FocusedEditSnapshot(
            focusWindow,
            text,
            selection.Value.Start,
            selection.Value.End);
    }

    internal static bool TryReplaceFocusedEditRange(
        IntPtr foregroundWindow,
        FocusedEditSnapshot expected,
        int replacementStart,
        int replacementLength,
        string replacement)
    {
        if (replacementStart < 0 ||
            replacementLength < 0 ||
            replacementStart > expected.Text.Length - replacementLength ||
            GetForegroundWindow() != foregroundWindow)
        {
            return false;
        }

        var current = TryReadFocusedEdit(foregroundWindow);
        if (current is null ||
            current.Window != expected.Window ||
            current.SelectionStart != expected.SelectionStart ||
            current.SelectionEnd != expected.SelectionEnd ||
            !current.Text.Equals(expected.Text, StringComparison.Ordinal))
        {
            return false;
        }

        var replacementEnd = replacementStart + replacementLength;
        var redrawSuspended = SendMessageTimeout(
            expected.Window,
            WmSetredraw,
            IntPtr.Zero,
            IntPtr.Zero,
            SmtoAbortIfHung,
            EditMessageTimeoutMs,
            out _) != IntPtr.Zero;
        if (!redrawSuspended)
        {
            ResumeEditRedraw(expected.Window);
            return false;
        }

        var replacementVerified = false;
        try
        {
            if (SendMessageTimeout(
                    expected.Window,
                    EmSetSel,
                    new IntPtr(replacementStart),
                    new IntPtr(replacementEnd),
                    SmtoAbortIfHung,
                    EditMessageTimeoutMs,
                    out _) == IntPtr.Zero)
            {
                return false;
            }

            if (SendMessageTimeoutString(
                    expected.Window,
                    EmReplacesel,
                    new IntPtr(1),
                    replacement,
                    SmtoAbortIfHung,
                    EditMessageTimeoutMs,
                    out _) == IntPtr.Zero)
            {
                return false;
            }

            var expectedText = expected.Text.Remove(
                    replacementStart,
                    replacementLength)
                .Insert(replacementStart, replacement);
            var result = TryReadFocusedEdit(foregroundWindow);
            replacementVerified = result is not null &&
                                  result.Window == expected.Window &&
                                  result.Text.Equals(expectedText, StringComparison.Ordinal) &&
                                  result.SelectionStart == replacementStart + replacement.Length &&
                                  result.SelectionEnd == result.SelectionStart;
            return replacementVerified;
        }
        finally
        {
            if (!replacementVerified)
            {
                SendMessageTimeout(
                    expected.Window,
                    EmSetSel,
                    new IntPtr(expected.SelectionStart),
                    new IntPtr(expected.SelectionEnd),
                    SmtoAbortIfHung,
                    EditMessageTimeoutMs,
                    out _);
            }

            ResumeEditRedraw(expected.Window);
        }
    }

    internal static bool TryRestoreFocusedEditSelection(
        IntPtr foregroundWindow,
        EditSelectionSnapshot snapshot)
    {
        if (GetForegroundWindow() != foregroundWindow ||
            TryGetFocusWindow(foregroundWindow) != snapshot.Window)
        {
            return false;
        }

        return SendMessageTimeout(
                   snapshot.Window,
                   EmSetSel,
                   new IntPtr(snapshot.Start),
                   new IntPtr(snapshot.End),
                   SmtoAbortIfHung,
                   SelectionMessageTimeoutMs,
                   out _) != IntPtr.Zero;
    }

    internal static bool SendChord(params ushort[] virtualKeys)
        => SendChordCore(InjectedInputMarker, virtualKeys);

    internal static bool SendUnmarkedChord(params ushort[] virtualKeys)
        => SendChordCore(IntPtr.Zero, virtualKeys);

    private static bool SendChordCore(IntPtr extraInfo, params ushort[] virtualKeys)
    {
        var inputs = new List<Input>(virtualKeys.Length * 2);

        foreach (var key in virtualKeys)
        {
            inputs.Add(CreateKeyboardInput(key, keyUp: false, extraInfo));
        }

        for (var index = virtualKeys.Length - 1; index >= 0; index--)
        {
            inputs.Add(CreateKeyboardInput(virtualKeys[index], keyUp: true, extraInfo));
        }

        return SendInputsReliably(inputs.ToArray());
    }

    private static IntPtr TryGetFocusWindow(IntPtr foregroundWindow)
    {
        if (foregroundWindow == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var info = new Guithreadinfo
        {
            Size = (uint)Marshal.SizeOf<Guithreadinfo>()
        };
        var threadId = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        return threadId != 0 && GetGUIThreadInfo(threadId, ref info)
            ? info.FocusWindow
            : IntPtr.Zero;
    }

    private static bool IsEditControl(IntPtr windowHandle)
    {
        var className = new StringBuilder(256);
        return GetClassName(windowHandle, className, className.Capacity) > 0 &&
               className.ToString().Contains("EDIT", StringComparison.OrdinalIgnoreCase);
    }

    private static EditSelectionSnapshot? TryCaptureEditSelection(
        IntPtr editWindow)
    {
        var startPointer = Marshal.AllocHGlobal(sizeof(int));
        var endPointer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            Marshal.WriteInt32(startPointer, 0);
            Marshal.WriteInt32(endPointer, 0);
            if (SendMessageTimeout(
                    editWindow,
                    EmGetSel,
                    startPointer,
                    endPointer,
                    SmtoAbortIfHung,
                    SelectionMessageTimeoutMs,
                    out _) == IntPtr.Zero)
            {
                return null;
            }

            return new EditSelectionSnapshot(
                editWindow,
                Marshal.ReadInt32(startPointer),
                Marshal.ReadInt32(endPointer));
        }
        finally
        {
            Marshal.FreeHGlobal(endPointer);
            Marshal.FreeHGlobal(startPointer);
        }
    }

    private static void ResumeEditRedraw(IntPtr editWindow)
    {
        SendMessageTimeout(
            editWindow,
            WmSetredraw,
            new IntPtr(1),
            IntPtr.Zero,
            SmtoAbortIfHung,
            EditMessageTimeoutMs,
            out _);
        SendNotifyMessage(
            editWindow,
            WmSetredraw,
            new IntPtr(1),
            IntPtr.Zero);
        InvalidateRect(editWindow, IntPtr.Zero, erase: true);
    }

    internal static bool SendModifiedKeyRepeated(
        ushort modifier,
        ushort virtualKey,
        int repeatCount)
    {
        if (repeatCount <= 0)
        {
            return true;
        }

        var inputs = new List<Input>((repeatCount * 2) + 2)
        {
            CreateKeyboardInput(modifier, keyUp: false, InjectedInputMarker)
        };
        for (var index = 0; index < repeatCount; index++)
        {
            inputs.Add(CreateKeyboardInput(virtualKey, keyUp: false, InjectedInputMarker));
            inputs.Add(CreateKeyboardInput(virtualKey, keyUp: true, InjectedInputMarker));
        }

        inputs.Add(CreateKeyboardInput(modifier, keyUp: true, InjectedInputMarker));
        return SendInputsReliably(inputs.ToArray());
    }

    internal static bool SendUnicodeText(string text)
    {
        if (text.Length == 0)
        {
            return true;
        }

        var characters = NormalizeLineBreaksForInput(text);
        var inputs = new Input[characters.Count * 2];
        for (var index = 0; index < characters.Count; index++)
        {
            inputs[index * 2] = CreateUnicodeInput(characters[index], keyUp: false);
            inputs[(index * 2) + 1] = CreateUnicodeInput(characters[index], keyUp: true);
        }

        return SendInputsReliably(inputs);
    }

    internal static bool SendTextReplacement(int caretMoveCount, string replacement)
    {
        if (caretMoveCount <= 0 || replacement.Length == 0)
        {
            return false;
        }

        var characters = NormalizeLineBreaksForInput(replacement);
        var inputs = new Input[(caretMoveCount * 2) + (characters.Count * 2)];
        var inputIndex = 0;
        for (var index = 0; index < caretMoveCount; index++)
        {
            inputs[inputIndex++] = CreateKeyboardInput(
                VkBack,
                keyUp: false,
                InjectedInputMarker);
            inputs[inputIndex++] = CreateKeyboardInput(
                VkBack,
                keyUp: true,
                InjectedInputMarker);
        }

        foreach (var character in characters)
        {
            inputs[inputIndex++] = CreateUnicodeInput(character, keyUp: false);
            inputs[inputIndex++] = CreateUnicodeInput(character, keyUp: true);
        }

        return SendInputsReliably(inputs);
    }

    private static bool SendInputsReliably(Input[] inputs)
    {
        var sentCount = 0;
        var attemptsWithoutProgress = 0;
        var inputSize = Marshal.SizeOf<Input>();

        while (sentCount < inputs.Length)
        {
            var remainingInputs = sentCount == 0
                ? inputs
                : inputs[sentCount..];
            var sentNow = SendInput(
                (uint)remainingInputs.Length,
                remainingInputs,
                inputSize);
            if (sentNow == 0)
            {
                attemptsWithoutProgress++;
                if (attemptsWithoutProgress >= SendInputRetryAttempts)
                {
                    return false;
                }

                Thread.Sleep(SendInputRetryDelayMs);
                continue;
            }

            if (sentNow > remainingInputs.Length)
            {
                return false;
            }

            sentCount += (int)sentNow;
            attemptsWithoutProgress = 0;
        }

        return true;
    }

    private static List<char> NormalizeLineBreaksForInput(string text)
    {
        var characters = new List<char>(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\n' && index > 0 && text[index - 1] == '\r')
            {
                continue;
            }

            characters.Add(character == '\n' ? '\r' : character);
        }

        return characters;
    }

    private static Input CreateKeyboardInput(
        ushort virtualKey,
        bool keyUp,
        IntPtr extraInfo) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new Keybdinput
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? KeyeventfKeyup : 0,
                ExtraInfo = extraInfo
            }
        }
    };

    private static Input CreateUnicodeInput(char character, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new Keybdinput
            {
                ScanCode = character,
                Flags = KeyeventfUnicode | (keyUp ? KeyeventfKeyup : 0),
                ExtraInfo = InjectedInputMarker
            }
        }
    };
}
