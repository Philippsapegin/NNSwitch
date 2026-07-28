using System.Globalization;

namespace INSwitch.Services;

internal sealed record LastTokenSelection(string Text, int CaretMoveCount)
{
    internal static int CountCaretMoves(string text) =>
        StringInfo.ParseCombiningCharacters(text).Length;

    internal static LastTokenSelection? FromTextBeforeCaret(string text)
    {
        var tokenEnd = text.Length;
        while (tokenEnd > 0 && char.IsWhiteSpace(text[tokenEnd - 1]))
        {
            tokenEnd--;
        }

        if (tokenEnd == 0)
        {
            return null;
        }

        var tokenStart = tokenEnd;
        while (tokenStart > 0 && !char.IsWhiteSpace(text[tokenStart - 1]))
        {
            tokenStart--;
        }

        var selectionText = text[tokenStart..];
        return new LastTokenSelection(
            selectionText,
            CountCaretMoves(selectionText));
    }
}
