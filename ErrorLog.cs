namespace INSwitch;

internal static class ErrorLog
{
    private const long MaximumLogSize = 1024 * 1024;

    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "NN Switch");

    internal static void Write(Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            var logPath = Path.Combine(LogDirectory, "error.log");
            if (File.Exists(logPath) && new FileInfo(logPath).Length >= MaximumLogSize)
            {
                File.Move(
                    logPath,
                    Path.Combine(LogDirectory, "error.previous.log"),
                    overwrite: true);
            }

            File.AppendAllText(
                logPath,
                $"[{DateTimeOffset.Now:O}]{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never take down the tray process.
        }
    }
}
