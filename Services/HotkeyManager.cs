using System.ComponentModel;
using INSwitch.Interop;
using INSwitch.Models;

namespace INSwitch.Services;

internal sealed record HotkeyCommand(
    TextSwitchMode Mode,
    string? TargetLayoutId,
    string DisplayName);

internal sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int FirstHotkeyId = 1001;

    private readonly Action<HotkeyCommand> _onHotkey;
    private readonly Dictionary<int, HotkeyCommand> _commands = new();
    private bool _disposed;

    internal HotkeyManager(Action<HotkeyCommand> onHotkey)
    {
        _onHotkey = onHotkey;
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
                new HotkeyCommand(action.Mode, null, action.CommandName),
                action.GetBinding(settings),
                errors);
        }

        foreach (var layout in layouts)
        {
            if (!settings.TargetLayouts.TryGetValue(layout.Id, out var targetHotkeys))
            {
                continue;
            }

            foreach (var action in TextSwitchActions.All)
            {
                Register(
                    nextId++,
                    new HotkeyCommand(
                        action.Mode,
                        layout.Id,
                        $"{layout.DisplayName}: {action.DisplayName.ToLowerInvariant()}"),
                    action.GetBinding(targetHotkeys),
                    errors);
            }
        }

        return errors;
    }

    internal void UnregisterAll()
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

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotkey &&
            _commands.TryGetValue(message.WParam.ToInt32(), out var command))
        {
            _onHotkey(command);
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
        HotkeyBinding binding,
        ICollection<string> errors)
    {
        if (!binding.IsConfigured)
        {
            return;
        }

        if (NativeMethods.RegisterHotKey(
                Handle,
                id,
                binding.Modifiers | HotkeyModifiers.NoRepeat,
                (uint)binding.Key))
        {
            _commands[id] = command;
            return;
        }

        var error = new Win32Exception().Message;
        errors.Add($"{command.DisplayName}: {HotkeyFormatter.Format(binding)} ({error})");
    }
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
