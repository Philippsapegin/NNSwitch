using INSwitch.Models;

namespace INSwitch.Services;

internal static class SettingsNormalizer
{
    internal static bool Normalize(
        AppSettings settings,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var changed = NormalizeSchema(settings);
        changed |= NormalizeCollections(settings);

        var installedIds = layouts
            .Select(layout => layout.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        changed |= RemoveStaleLayouts(settings, installedIds);
        changed |= NormalizeSwitchTargets(settings, layouts, installedIds);
        changed |= NormalizeTargetHotkeys(settings, layouts);
        return changed;
    }

    private static bool NormalizeSchema(AppSettings settings)
    {
        if (settings.SchemaVersion == AppSettings.CurrentSchemaVersion)
        {
            return false;
        }

        settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
        return true;
    }

    private static bool NormalizeCollections(AppSettings settings)
    {
        var changed = false;
        var defaults = HotkeySettings.Defaults;
        if (settings.Hotkeys is null)
        {
            settings.Hotkeys = defaults;
            changed = true;
        }

        if (settings.Hotkeys.SelectedText is null)
        {
            settings.Hotkeys.SelectedText = defaults.SelectedText;
            changed = true;
        }

        if (settings.Hotkeys.LastWord is null)
        {
            settings.Hotkeys.LastWord = defaults.LastWord;
            changed = true;
        }

        if (settings.Hotkeys.ActiveField is null)
        {
            settings.Hotkeys.ActiveField = defaults.ActiveField;
            changed = true;
        }

        if (settings.Hotkeys.TargetLayouts is null)
        {
            settings.Hotkeys.TargetLayouts =
                new Dictionary<string, TargetLayoutHotkeys>(StringComparer.OrdinalIgnoreCase);
            changed = true;
        }

        settings.Hotkeys.TargetLayouts = new Dictionary<string, TargetLayoutHotkeys>(
            settings.Hotkeys.TargetLayouts,
            StringComparer.OrdinalIgnoreCase);

        if (settings.SwitchTargets is null)
        {
            settings.SwitchTargets =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            changed = true;
        }

        settings.SwitchTargets = new Dictionary<string, string>(
            settings.SwitchTargets,
            StringComparer.OrdinalIgnoreCase);
        return changed;
    }

    private static bool RemoveStaleLayouts(
        AppSettings settings,
        IReadOnlySet<string> installedIds)
    {
        var changed = false;
        foreach (var staleId in settings.SwitchTargets.Keys
                     .Where(id => !installedIds.Contains(id))
                     .ToList())
        {
            settings.SwitchTargets.Remove(staleId);
            changed = true;
        }

        foreach (var staleId in settings.Hotkeys.TargetLayouts.Keys
                     .Where(id => !installedIds.Contains(id))
                     .ToList())
        {
            settings.Hotkeys.TargetLayouts.Remove(staleId);
            changed = true;
        }

        return changed;
    }

    private static bool NormalizeSwitchTargets(
        AppSettings settings,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts,
        IReadOnlySet<string> installedIds)
    {
        var changed = false;
        foreach (var layout in layouts)
        {
            if (!settings.SwitchTargets.TryGetValue(layout.Id, out var targetId) ||
                (!string.IsNullOrWhiteSpace(targetId) && !installedIds.Contains(targetId)))
            {
                settings.SwitchTargets[layout.Id] =
                    ChooseDefaultTarget(layout, layouts)?.Id ?? string.Empty;
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeTargetHotkeys(
        AppSettings settings,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var changed = false;
        foreach (var layout in layouts)
        {
            if (!settings.Hotkeys.TargetLayouts.TryGetValue(layout.Id, out var targetHotkeys) ||
                targetHotkeys is null)
            {
                settings.Hotkeys.TargetLayouts[layout.Id] = new TargetLayoutHotkeys();
                changed = true;
                continue;
            }

            changed |= NormalizeBindings(targetHotkeys);
        }

        return changed;
    }

    private static bool NormalizeBindings(TargetLayoutHotkeys targetHotkeys)
    {
        var changed = false;
        if (targetHotkeys.SelectedText is null)
        {
            targetHotkeys.SelectedText = new HotkeyBinding();
            changed = true;
        }

        if (targetHotkeys.LastWord is null)
        {
            targetHotkeys.LastWord = new HotkeyBinding();
            changed = true;
        }

        if (targetHotkeys.ActiveField is null)
        {
            targetHotkeys.ActiveField = new HotkeyBinding();
            changed = true;
        }

        return changed;
    }

    private static KeyboardLayoutDescriptor? ChooseDefaultTarget(
        KeyboardLayoutDescriptor source,
        IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var preferredLanguage = source.TwoLetterLanguage switch
        {
            "ru" => "en",
            "en" => "ru",
            _ => string.Empty
        };

        if (preferredLanguage.Length > 0)
        {
            var preferred = layouts.FirstOrDefault(layout =>
                !layout.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase) &&
                layout.TwoLetterLanguage == preferredLanguage);

            if (preferred is not null)
            {
                return preferred;
            }
        }

        return layouts.FirstOrDefault(layout =>
            !layout.Id.Equals(source.Id, StringComparison.OrdinalIgnoreCase) &&
            layout.TwoLetterLanguage != source.TwoLetterLanguage);
    }
}
