using System.IO;

namespace oru.Services;

public static class DebugLogService
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "oru",
        "oru.log");

    private static readonly object _lockObj = new();

    public static void Write(string message)
    {
        lock (_lockObj)
        {
            try
        {
            var directory = Path.GetDirectoryName(LogFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.AppendAllText(LogFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
        }
            catch
            {
                // Logging should never crash the app.
            }
        }
    }

    public static string GetLogPath()
    {
        return LogFilePath;
    }
}
