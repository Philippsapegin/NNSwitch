using System.Text.Json;
using INSwitch.Models;

namespace INSwitch.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NN Switch");

    private string SettingsPath => Path.Combine(_settingsDirectory, "settings.json");

    internal AppSettings Load(IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        AppSettings settings;

        try
        {
            var sourcePath = GetSettingsSourcePath();
            settings = sourcePath is not null
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(sourcePath), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception exception)
        {
            ErrorLog.Write(exception);
            settings = new AppSettings();
        }

        Normalize(settings, layouts);
        return settings;
    }

    internal void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
        }
        catch (Exception exception)
        {
            ErrorLog.Write(exception);
        }
    }

    internal static void Normalize(AppSettings settings, IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var defaults = HotkeySettings.Defaults;
        settings.Hotkeys ??= defaults;
        settings.Hotkeys.SelectedText ??= defaults.SelectedText;
        settings.Hotkeys.LastWord ??= defaults.LastWord;
        settings.Hotkeys.ActiveField ??= defaults.ActiveField;
        settings.Hotkeys.TargetLayouts = settings.Hotkeys.TargetLayouts is null
            ? new Dictionary<string, TargetLayoutHotkeys>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, TargetLayoutHotkeys>(
                settings.Hotkeys.TargetLayouts,
                StringComparer.OrdinalIgnoreCase);

        settings.SwitchTargets = settings.SwitchTargets is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(settings.SwitchTargets, StringComparer.OrdinalIgnoreCase);

        var installedIds = layouts.Select(layout => layout.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var staleId in settings.SwitchTargets.Keys.Where(id => !installedIds.Contains(id)).ToList())
        {
            settings.SwitchTargets.Remove(staleId);
        }

        foreach (var layout in layouts)
        {
            if (settings.SwitchTargets.ContainsKey(layout.Id))
            {
                continue;
            }

            settings.SwitchTargets[layout.Id] = ChooseDefaultTarget(layout, layouts)?.Id ?? string.Empty;
        }

        foreach (var layout in layouts)
        {
            if (!settings.Hotkeys.TargetLayouts.TryGetValue(layout.Id, out var targetHotkeys) ||
                targetHotkeys is null)
            {
                settings.Hotkeys.TargetLayouts[layout.Id] = new TargetLayoutHotkeys();
                continue;
            }

            targetHotkeys.SelectedText ??= new HotkeyBinding();
            targetHotkeys.LastWord ??= new HotkeyBinding();
            targetHotkeys.ActiveField ??= new HotkeyBinding();
        }
    }

    private string? GetSettingsSourcePath()
    {
        if (File.Exists(SettingsPath))
        {
            return SettingsPath;
        }

        var legacyPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ИN Switch",
            "settings.json");
        return File.Exists(legacyPath) ? legacyPath : null;
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
