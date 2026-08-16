using System.ComponentModel;
using System.Runtime.InteropServices;
using INSwitch.Interop;
using INSwitch.Models;

namespace INSwitch.Services;

internal abstract record HotkeyCommand(string DisplayName);

internal sealed record TextSwitchHotkeyCommand(
    TextSwitchMode Mode,
    string? TargetLayoutId,
    string CommandDisplayName) : HotkeyCommand(CommandDisplayName);

internal sealed record TextCaseHotkeyCommand(
    TextCaseMode Mode,
    string CommandDisplayName) : HotkeyCommand(CommandDisplayName);

internal sealed record KeyboardLayoutHotkeyCommand(
    string TargetLayoutId,
    string CommandDisplayName) : HotkeyCommand(CommandDisplayName);

internal sealed record CycleKeyboardLayoutHotkeyCommand(
    string CommandDisplayName) : HotkeyCommand(CommandDisplayName);

internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int FirstHotkeyId = 1001;

    private readonly Action<HotkeyCommand> _onHotkey;
    private readonly TypedInputTracker _typedInputTracker;
    private readonly Dictionary<int, HotkeyCommand> _commands = new();
    private readonly Dictionary<int, ExclusiveHotkey> _exclusiveCommands = new();
    private readonly Dictionary<uint, int> _pendingHotkeys = new();
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardHookCallback;
    private readonly NativeMethods.LowLevelMouseProc _mouseHookCallback;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _commandHandlingSuspended;
    private bool _disposed;

    internal HotkeyManager(
        Action<HotkeyCommand> onHotkey,
        TypedInputTracker typedInputTracker)
    {
        _onHotkey = onHotkey;
        _typedInputTracker = typedInputTracker;
        _keyboardHookCallback = KeyboardHookCallback;
        _mouseHookCallback = MouseHookCallback;
        CreateHandle(new CreateParams
        {
            Caption = "NN Switch Hotkeys",
            Parent = new IntPtr(-3)
        });
    }

    internal IReadOnlyList<string> RegisterAll(
        HotkeySettings settings,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        UnregisterAll();
        var errors = new List<string>();
        var nextId = FirstHotkeyId;

        foreach (var action in TextSwitchActions.All)
        {
            Register(
                nextId++,
                new TextSwitchHotkeyCommand(action.Mode, null, action.CommandName),
                action.GetBinding(settings));
        }

        foreach (var action in TextCaseActions.All)
        {
            Register(
                nextId++,
                new TextCaseHotkeyCommand(action.Mode, action.CommandName),
                action.GetBinding(settings));
        }

        Register(
            nextId++,
            new CycleKeyboardLayoutHotkeyCommand("Cycle input language"),
            settings.CycleLayout);

        foreach (var layout in layouts)
        {
            if (!settings.TargetLayouts.TryGetValue(layout.Id, out var targetHotkeys))
            {
                continue;
            }

            Register(
                nextId++,
                new KeyboardLayoutHotkeyCommand(
                    layout.Id,
                    $"Switch input language to {layout.DisplayName}"),
                targetHotkeys.ActivateLayout);

            foreach (var action in TextSwitchActions.All)
            {
                Register(
                    nextId++,
                    new TextSwitchHotkeyCommand(
                        action.Mode,
                        layout.Id,
                        $"{layout.DisplayName}: {action.DisplayName.ToLowerInvariant()}"),
                    action.GetBinding(targetHotkeys));
            }
        }

        if (_exclusiveCommands.Count == 0)
        {
            return errors;
        }

        var exclusiveHookInstalled = TryInstallExclusiveHook();
        RegisterFallbackHotkeys(exclusiveHookInstalled, errors);

        return errors;
    }

    internal void SetCommandHandlingSuspended(bool suspended)
    {
        if (_disposed || _commandHandlingSuspended == suspended)
        {
            return;
        }

        _commandHandlingSuspended = suspended;
        _pendingHotkeys.Clear();
        if (suspended)
        {
            UnregisterFallbackHotkeys();
            return;
        }

        RegisterFallbackHotkeys(
            exclusiveHookInstalled: _keyboardHook != IntPtr.Zero,
            errors: null);
    }

    internal void UnregisterAll()
    {
        UnregisterFallbackHotkeys();
        _exclusiveCommands.Clear();
        _pendingHotkeys.Clear();
        _commandHandlingSuspended = false;
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _typedInputTracker.SetPointerHookActive(active: false);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey &&
            !_commandHandlingSuspended &&
            _commands.TryGetValue(message.WParam.ToInt32(), out var command))
        {
            _onHotkey(command);
            return;
        }

        if (message.Msg == NativeMethods.WmAppExclusiveHotkey &&
            !_commandHandlingSuspended &&
            _exclusiveCommands.TryGetValue(message.WParam.ToInt32(), out var exclusive))
        {
            _onHotkey(exclusive.Command);
            return;
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        UnregisterAll();
        DestroyHandle();
        GC.SuppressFinalize(this);
    }

    private void Register(
        int id,
        HotkeyCommand command,
        HotkeyBinding binding)
    {
        if (!binding.IsConfigured)
        {
            return;
        }

        _exclusiveCommands[id] = new ExclusiveHotkey(command, binding.Clone());
    }

    private bool TryInstallExclusiveHook()
    {
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _keyboardHookCallback,
            NativeMethods.GetModuleHandle(null),
            0);
        if (_keyboardHook == IntPtr.Zero)
        {
            return false;
        }

        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseHookCallback,
            NativeMethods.GetModuleHandle(null),
            0);
        _typedInputTracker.SetPointerHookActive(_mouseHook != IntPtr.Zero);
        return true;
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0)
        {
            var message = wParam.ToInt32();
            var keyboardData = Marshal.PtrToStructure<NativeMethods.Kbdllhookstruct>(lParam);
            if (keyboardData.ExtraInfo == NativeMethods.InjectedInputMarker)
            {
                return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
            }

            if (_commandHandlingSuspended)
            {
                _typedInputTracker.ObserveKeyboardInput(message, keyboardData);
                return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
            }

            if (message is NativeMethods.WmKeyup or NativeMethods.WmSyskeyup)
            {
                if (_pendingHotkeys.Remove(keyboardData.VirtualKey, out var hotkeyId))
                {
                    NativeMethods.PostMessage(
                        Handle,
                        NativeMethods.WmAppExclusiveHotkey,
                        new IntPtr(hotkeyId),
                        IntPtr.Zero);
                    return new IntPtr(1);
                }

                _typedInputTracker.ObserveKeyboardInput(message, keyboardData);
            }
            else if (message is NativeMethods.WmKeydown or NativeMethods.WmSyskeydown)
            {
                if (_pendingHotkeys.ContainsKey(keyboardData.VirtualKey))
                {
                    return new IntPtr(1);
                }

                var modifiers = GetPressedModifiers();
                foreach (var (id, exclusive) in _exclusiveCommands)
                {
                    if ((uint)exclusive.Binding.Key != keyboardData.VirtualKey ||
                        exclusive.Binding.Modifiers != modifiers)
                    {
                        continue;
                    }

                    _pendingHotkeys[keyboardData.VirtualKey] = id;
                    return new IntPtr(1);
                }

                _typedInputTracker.ObserveKeyboardInput(message, keyboardData);
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, wParam, lParam);
    }

    private void RegisterFallbackHotkeys(
        bool exclusiveHookInstalled,
        ICollection<string>? errors)
    {
        if (_commandHandlingSuspended || Handle == IntPtr.Zero)
        {
            return;
        }

        UnregisterFallbackHotkeys();
        foreach (var (id, exclusive) in _exclusiveCommands)
        {
            if (NativeMethods.RegisterHotKey(
                    Handle,
                    id,
                    exclusive.Binding.Modifiers | HotkeyModifiers.NoRepeat,
                    (uint)exclusive.Binding.Key))
            {
                _commands[id] = exclusive.Command;
                continue;
            }

            if (!exclusiveHookInstalled && errors is not null)
            {
                var error = new Win32Exception().Message;
                errors.Add(
                    $"{exclusive.Command.DisplayName}: " +
                    $"{HotkeyFormatter.Format(exclusive.Binding)} ({error})");
            }
        }
    }

    private void UnregisterFallbackHotkeys()
    {
        if (Handle != IntPtr.Zero)
        {
            foreach (var id in _commands.Keys)
            {
                NativeMethods.UnregisterHotKey(Handle, id);
            }
        }

        _commands.Clear();
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 &&
            wParam.ToInt32() is NativeMethods.WmLbuttondown or
                NativeMethods.WmRbuttondown or
                NativeMethods.WmMbuttondown or
                NativeMethods.WmXbuttondown or
                NativeMethods.WmMousewheel or
                NativeMethods.WmMousehwheel)
        {
            _typedInputTracker.ObservePointerInput();
        }

        return NativeMethods.CallNextHookEx(_mouseHook, code, wParam, lParam);
    }

    private static HotkeyModifiers GetPressedModifiers()
    {
        var modifiers = HotkeyModifiers.None;
        if (NativeMethods.IsKeyDown(NativeMethods.VkControl))
        {
            modifiers |= HotkeyModifiers.Control;
        }

        if (NativeMethods.IsKeyDown(NativeMethods.VkShift))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (NativeMethods.IsKeyDown(NativeMethods.VkMenu))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (NativeMethods.IsKeyDown(NativeMethods.VkLwin) ||
            NativeMethods.IsKeyDown(NativeMethods.VkRwin))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    private sealed record ExclusiveHotkey(
        HotkeyCommand Command,
        HotkeyBinding Binding);
}

internal static class HotkeyFormatter
{
    internal static string Format(HotkeyBinding binding)
    {
        if (!binding.IsConfigured)
        {
            return string.Empty;
        }

        var parts = new List<string>(5);
        if (binding.Modifiers.HasFlag(HotkeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (binding.Modifiers.HasFlag(HotkeyModifiers.Win))
        {
            parts.Add("Win");
        }

        parts.Add(new KeysConverter().ConvertToInvariantString(binding.Key) ?? binding.Key.ToString());
        return string.Join(" + ", parts);
    }
}
