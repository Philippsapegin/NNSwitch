namespace INSwitch.Models;

internal enum TextSwitchMode
{
    SelectedText,
    LastWord,
    ActiveField
}

internal sealed record TextSwitchAction(
    TextSwitchMode Mode,
    string DisplayName,
    string CommandName)
{
    internal HotkeyBinding GetBinding(HotkeySettings settings) => Mode switch
    {
        TextSwitchMode.SelectedText => settings.SelectedText,
        TextSwitchMode.LastWord => settings.LastWord,
        TextSwitchMode.ActiveField => settings.ActiveField,
        _ => throw new ArgumentOutOfRangeException(nameof(Mode))
    };

    internal HotkeyBinding GetBinding(TargetLayoutHotkeys settings) => Mode switch
    {
        TextSwitchMode.SelectedText => settings.SelectedText,
        TextSwitchMode.LastWord => settings.LastWord,
        TextSwitchMode.ActiveField => settings.ActiveField,
        _ => throw new ArgumentOutOfRangeException(nameof(Mode))
    };

    internal void SetBinding(HotkeySettings settings, HotkeyBinding binding)
    {
        switch (Mode)
        {
            case TextSwitchMode.SelectedText:
                settings.SelectedText = binding;
                break;
            case TextSwitchMode.LastWord:
                settings.LastWord = binding;
                break;
            case TextSwitchMode.ActiveField:
                settings.ActiveField = binding;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Mode));
        }
    }

    internal void SetBinding(TargetLayoutHotkeys settings, HotkeyBinding binding)
    {
        switch (Mode)
        {
            case TextSwitchMode.SelectedText:
                settings.SelectedText = binding;
                break;
            case TextSwitchMode.LastWord:
                settings.LastWord = binding;
                break;
            case TextSwitchMode.ActiveField:
                settings.ActiveField = binding;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Mode));
        }
    }
}

internal static class TextSwitchActions
{
    internal static readonly IReadOnlyList<TextSwitchAction> All =
    [
        new(TextSwitchMode.SelectedText, "Selected text", "Switch selected text"),
        new(TextSwitchMode.LastWord, "Last written word", "Switch last written word"),
        new(TextSwitchMode.ActiveField, "Active text field", "Switch active text field")
    ];
}
