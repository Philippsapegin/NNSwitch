using System.Globalization;

namespace INSwitch.Services;

internal sealed record LastTokenSelection(string Text, int CaretMoveCount)
{
    private static readonly IReadOnlyDictionary<char, char> DelimiterPairs =
        new Dictionary<char, char>
        {
            ['('] = ')',
            ['['] = ']',
            ['{'] = '}',
            ['<'] = '>',
            ['"'] = '"',
            ['\''] = '\'',
            ['`'] = '`',
            ['«'] = '»',
            ['“'] = '”',
            ['„'] = '“',
            ['‘'] = '’'
        };

    private static readonly IReadOnlySet<char> ClosingDelimiters =
        DelimiterPairs.Values.ToHashSet();

    internal static int CountCaretMoves(string text) =>
        StringInfo.ParseCombiningCharacters(text).Length;

    internal static bool IsClosingDelimiter(char character) =>
        ClosingDelimiters.Contains(character);

    internal static bool IsOpeningDelimiter(char character) =>
        DelimiterPairs.ContainsKey(character);

    internal static LastTokenSelection? FromTextBeforeCaret(string text) =>
        FromTextAroundCaret(text, string.Empty);

    internal static LastTokenSelection? FromTextAroundCaret(
        string textBeforeCaret,
        string textAfterCaret)
    {
        var tokenEnd = textBeforeCaret.Length;
        while (tokenEnd > 0 && char.IsWhiteSpace(textBeforeCaret[tokenEnd - 1]))
        {
            tokenEnd--;
        }

        if (tokenEnd == 0)
        {
            return null;
        }

        var tokenStart = tokenEnd;
        while (tokenStart > 0 && !char.IsWhiteSpace(textBeforeCaret[tokenStart - 1]))
        {
            tokenStart--;
        }

        tokenStart += CountPairedOpeningDelimiters(
            textBeforeCaret.AsSpan(tokenStart, tokenEnd - tokenStart),
            textAfterCaret.AsSpan());

        if (tokenStart == tokenEnd)
        {
            return null;
        }

        var selectionText = textBeforeCaret[tokenStart..];
        return new LastTokenSelection(
            selectionText,
            CountCaretMoves(selectionText));
    }

    private static int CountPairedOpeningDelimiters(
        ReadOnlySpan<char> token,
        ReadOnlySpan<char> textAfterCaret)
    {
        var openingCount = 0;
        while (openingCount < token.Length &&
               DelimiterPairs.ContainsKey(token[openingCount]))
        {
            openingCount++;
        }

        var maximumPairCount = Math.Min(openingCount, textAfterCaret.Length);
        for (var pairCount = maximumPairCount; pairCount > 0; pairCount--)
        {
            var allMatch = true;
            for (var index = 0; index < pairCount; index++)
            {
                var opener = token[pairCount - index - 1];
                if (DelimiterPairs[opener] != textAfterCaret[index])
                {
                    allMatch = false;
                    break;
                }
            }

            if (allMatch)
            {
                return pairCount;
            }
        }

        return 0;
    }
}
