using System.Globalization;
using System.Text;
using INSwitch.Models;

namespace INSwitch.Services;

internal static class TextCaseConverter
{
    internal static string Convert(
        string text,
        TextCaseMode mode,
        CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        return mode switch
        {
            TextCaseMode.UpperCase => culture.TextInfo.ToUpper(text),
            TextCaseMode.LowerCase => culture.TextInfo.ToLower(text),
            TextCaseMode.SentenceCase => ToSentenceCase(text, culture),
            _ => throw new ArgumentOutOfRangeException(nameof(mode))
        };
    }

    private static string ToSentenceCase(string text, CultureInfo culture)
    {
        var lowerCaseText = culture.TextInfo.ToLower(text);
        var result = new StringBuilder(lowerCaseText.Length);
        var capitalizeNextLetter = true;
        var elements = StringInfo.GetTextElementEnumerator(lowerCaseText);

        while (elements.MoveNext())
        {
            var element = elements.GetTextElement();
            if (capitalizeNextLetter && ContainsLetter(element))
            {
                result.Append(culture.TextInfo.ToUpper(element));
                capitalizeNextLetter = false;
            }
            else
            {
                result.Append(element);
            }

            if (element.Any(IsSentenceBoundary))
            {
                capitalizeNextLetter = true;
            }
        }

        return result.ToString();
    }

    private static bool ContainsLetter(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSentenceBoundary(char character) => character is
        '.' or '!' or '?' or '\r' or '\n' or '\u2028' or '\u2029' or
        '\u3002' or '\uFF01' or '\uFF1F';
}
