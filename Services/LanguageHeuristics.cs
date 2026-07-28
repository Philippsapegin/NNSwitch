using System.Text;

namespace INSwitch.Services;

internal static class LanguageHeuristics
{
    private sealed record Profile(
        HashSet<string> Words,
        HashSet<string> Bigrams,
        HashSet<string> Trigrams);

    private static readonly Dictionary<string, Profile> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = CreateProfile(
            """
            a about after again all also am an and any are as at back be because been before being
            between both but by can come could day did do does done down each even every first for
            from get give go good got great had has have he hello help her here him his how i if in
            into is it its just know last like little long look made make many may me more most much
            must my new no not now of off on one only or other our out over people right said same
            see she should so some still such take than that the their them then there these they
            thing think this those through time to too two up us use very want was way we well were
            what when where which while who why will with word work world would year yes you your
            """,
            "th he in er an re on at en nd ti es or te of ed is it al ar st to nt ng se ha as ou io le ve co me de hi ri ro ic ne ea ra ce li ch ll be ma si om ur",
            "the and ing her ere ent tha nth was eth for dth hat she ion tio ver est ers ati his all ith hes ter you wit thi rea not one our out are but had have"),

        ["ru"] = CreateProfile(
            """
            а без был была были было быть в вам вас весь во вот все всего всегда вы где да даже
            два для до его ее если есть еще же за здесь и из или им их к как когда кто ли мы на
            над надо наш не него нее нет но ну о об один она они оно от по под после потом потому
            при про раз с сам себе себя сейчас со так такой там те тем то того тоже только том тут
            ты у уже хорошо хотя чем что чтобы это этот я привет мир работа слово текст язык
            """,
            "ст но то на ен ов ни ра во ко пр ро по ер ал ли ор го ос ет ре ка та ол те ел ит от ва ан де ес ве ла не",
            "про ост ени ого ста сто ние при ова стр ест тов тер ать его это как что для был она они при вет мир раб тек язы"),
    };

    internal static bool ShouldSwitch(
        string sourceWord,
        string convertedWord,
        KeyboardLayoutDescriptor source,
        KeyboardLayoutDescriptor target)
    {
        if (source.TwoLetterLanguage.Equals(target.TwoLetterLanguage, StringComparison.OrdinalIgnoreCase) ||
            !Profiles.TryGetValue(source.TwoLetterLanguage, out var sourceProfile) ||
            !Profiles.TryGetValue(target.TwoLetterLanguage, out var targetProfile))
        {
            return false;
        }

        var normalizedSource = NormalizeWord(sourceWord);
        var normalizedTarget = NormalizeWord(convertedWord);
        if (normalizedSource.Length < 3 || normalizedTarget.Length < 3)
        {
            return false;
        }

        var sourceIsKnown = sourceProfile.Words.Contains(normalizedSource);
        var targetIsKnown = targetProfile.Words.Contains(normalizedTarget);
        if (targetIsKnown && !sourceIsKnown)
        {
            return true;
        }

        if (sourceIsKnown)
        {
            return false;
        }

        var sourceScore = Score(normalizedSource, sourceProfile);
        var targetScore = Score(normalizedTarget, targetProfile);
        var requiredAdvantage = normalizedSource.Length <= 3 ? 4.0 : 2.2;
        return targetScore >= 1.0 && targetScore - sourceScore >= requiredAdvantage;
    }

    private static double Score(string word, Profile profile)
    {
        var score = profile.Words.Contains(word) ? 8.0 : 0.0;

        for (var index = 0; index + 1 < word.Length; index++)
        {
            if (profile.Bigrams.Contains(word.Substring(index, 2)))
            {
                score += 0.55;
            }
        }

        for (var index = 0; index + 2 < word.Length; index++)
        {
            if (profile.Trigrams.Contains(word.Substring(index, 3)))
            {
                score += 1.1;
            }
        }

        return score;
    }

    private static string NormalizeWord(string value)
    {
        var result = new StringBuilder(value.Length);
        foreach (var character in value.ToLowerInvariant())
        {
            if (char.IsLetter(character))
            {
                result.Append(character);
            }
        }

        return result.ToString();
    }

    private static Profile CreateProfile(string words, string bigrams, string trigrams) => new(
        Split(words),
        Split(bigrams),
        Split(trigrams));

    private static HashSet<string> Split(string value) =>
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
}
