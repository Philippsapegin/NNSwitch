namespace INSwitch.Models;

internal enum TextCaseMode
{
    UpperCase,
    LowerCase,
    SentenceCase
}

internal sealed record TextCaseAction(
    TextCaseMode Mode,
    string DisplayName,
    string CommandName)
{
    internal HotkeyBinding GetBinding(HotkeySettings settings) => Mode switch
    {
        TextCaseMode.UpperCase => settings.UpperCase,
        TextCaseMode.LowerCase => settings.LowerCase,
        TextCaseMode.SentenceCase => settings.SentenceCase,
        _ => throw new ArgumentOutOfRangeException(nameof(Mode))
    };

    internal void SetBinding(HotkeySettings settings, HotkeyBinding binding)
    {
        switch (Mode)
        {
            case TextCaseMode.UpperCase:
                settings.UpperCase = binding;
                break;
            case TextCaseMode.LowerCase:
                settings.LowerCase = binding;
                break;
            case TextCaseMode.SentenceCase:
                settings.SentenceCase = binding;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(Mode));
        }
    }
}

internal static class TextCaseActions
{
    internal static readonly IReadOnlyList<TextCaseAction> All =
    [
        new(TextCaseMode.UpperCase, "UPPERCASE", "Change selected text to UPPERCASE"),
        new(TextCaseMode.LowerCase, "lowercase", "Change selected text to lowercase"),
        new(TextCaseMode.SentenceCase, "Sentence case", "Change selected text to sentence case")
    ];
}
