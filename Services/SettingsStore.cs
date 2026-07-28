using System.Text.Json;
using INSwitch.Models;

namespace INSwitch.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _settingsDirectory;
    private readonly string _legacySettingsDirectory;
    private readonly Action<Exception> _logError;

    private string SettingsPath => Path.Combine(_settingsDirectory, "settings.json");

    internal SettingsStore(
        string? settingsDirectory = null,
        string? legacySettingsDirectory = null,
        Action<Exception>? logError = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _settingsDirectory = settingsDirectory ?? Path.Combine(appData, "NN Switch");
        _legacySettingsDirectory = legacySettingsDirectory ?? Path.Combine(appData, "ИN Switch");
        _logError = logError ?? ErrorLog.Write;
    }

    internal AppSettings Load(IReadOnlyList<KeyboardLayoutDescriptor> layouts)
    {
        var sourcePath = GetSettingsSourcePath();
        if (sourcePath is null)
        {
            var newSettings = new AppSettings();
            SettingsNormalizer.Normalize(newSettings, layouts);
            Save(newSettings);
            return newSettings;
        }

        try
        {
            var settings =
                JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(sourcePath), JsonOptions) ??
                throw new JsonException("settings.json contains a null root value.");
            var changed = SettingsNormalizer.Normalize(settings, layouts);
            if (changed || !sourcePath.Equals(SettingsPath, StringComparison.OrdinalIgnoreCase))
            {
                Save(settings);
            }

            return settings;
        }
        catch (JsonException exception)
        {
            _logError(exception);
            var settings = new AppSettings();
            SettingsNormalizer.Normalize(settings, layouts);
            if (TryBackUpCorruptSettings(sourcePath))
            {
                Save(settings);
            }

            return settings;
        }
        catch (Exception exception)
        {
            _logError(exception);
            var settings = new AppSettings();
            SettingsNormalizer.Normalize(settings, layouts);
            return settings;
        }
    }

    internal bool Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(_settingsDirectory);
            var temporaryPath = SettingsPath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporaryPath, SettingsPath, overwrite: true);
            return true;
        }
        catch (Exception exception)
        {
            _logError(exception);
            return false;
        }
    }

    private string? GetSettingsSourcePath()
    {
        if (File.Exists(SettingsPath))
        {
            return SettingsPath;
        }

        var legacyPath = Path.Combine(_legacySettingsDirectory, "settings.json");
        return File.Exists(legacyPath) ? legacyPath : null;
    }

    private bool TryBackUpCorruptSettings(string sourcePath)
    {
        try
        {
            var timestamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss");
            File.Copy(sourcePath, $"{sourcePath}.corrupt-{timestamp}", overwrite: false);
            return true;
        }
        catch (Exception exception)
        {
            _logError(exception);
            return false;
        }
    }
}
