using System.Runtime.InteropServices;
using System.Text;
using INSwitch.Models;

namespace INSwitch.Interop;

internal static class NativeMethods
{
    internal const int WmHotkey = 0x0312;
    internal const int WmInputLanguageChangeRequest = 0x0050;

    internal const ushort VkShift = 0x10;
    internal const ushort VkControl = 0x11;
    internal const ushort VkMenu = 0x12;
    internal const ushort VkLeft = 0x25;
    internal const ushort VkLwin = 0x5B;
    internal const ushort VkRwin = 0x5C;
    internal const ushort VkA = 0x41;
    internal const ushort VkC = 0x43;
    internal const ushort VkV = 0x56;
    internal const ushort VkX = 0x58;

    internal const uint KeyeventfKeyup = 0x0002;
    internal const uint InputKeyboard = 1;
    internal const uint MapvkVkToVsc = 0;

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
    private struct GuiThreadInfo
    {
        internal uint Size;
        internal uint Flags;
        internal IntPtr ActiveWindow;
        internal IntPtr FocusedWindow;
        internal IntPtr CaptureWindow;
        internal IntPtr MenuOwnerWindow;
        internal IntPtr MoveSizeWindow;
        internal IntPtr CaretWindow;
        internal Rect CaretRectangle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

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

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr windowHandle, IntPtr processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint threadId, ref GuiThreadInfo info);

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
    internal static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint inputCount, Input[] inputs, int inputSize);

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

    internal static IntPtr GetFocusedControl(IntPtr foregroundWindow)
    {
        var threadId = GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
        var info = new GuiThreadInfo
        {
            Size = (uint)Marshal.SizeOf<GuiThreadInfo>()
        };
        return threadId != 0 && GetGUIThreadInfo(threadId, ref info)
            ? info.FocusedWindow
            : IntPtr.Zero;
    }

    internal static bool SendChord(params ushort[] virtualKeys)
    {
        var inputs = new List<Input>(virtualKeys.Length * 2);

        foreach (var key in virtualKeys)
        {
            inputs.Add(CreateKeyboardInput(key, keyUp: false));
        }

        for (var index = virtualKeys.Length - 1; index >= 0; index--)
        {
            inputs.Add(CreateKeyboardInput(virtualKeys[index], keyUp: true));
        }

        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) == inputs.Count;
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
            CreateKeyboardInput(modifier, keyUp: false)
        };
        for (var index = 0; index < repeatCount; index++)
        {
            inputs.Add(CreateKeyboardInput(virtualKey, keyUp: false));
            inputs.Add(CreateKeyboardInput(virtualKey, keyUp: true));
        }

        inputs.Add(CreateKeyboardInput(modifier, keyUp: true));
        return SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<Input>()) == inputs.Count;
    }

    private static Input CreateKeyboardInput(ushort virtualKey, bool keyUp) => new()
    {
        Type = InputKeyboard,
        Data = new InputUnion
        {
            Keyboard = new Keybdinput
            {
                VirtualKey = virtualKey,
                Flags = keyUp ? KeyeventfKeyup : 0
            }
        }
    };
}
