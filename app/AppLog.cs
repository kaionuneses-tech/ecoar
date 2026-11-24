namespace Ecoar;

/// <summary>Small diagnostic log in %APPDATA%\Ecoar\app.log (startup sound count, saves, unexpected errors).</summary>
public static class AppLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();
    public static string PathName { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Ecoar", "app.log");

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
                if (File.Exists(PathName) && new FileInfo(PathName).Length > MaxBytes) File.Delete(PathName);
                File.AppendAllText(PathName, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }               // logging must never break the app
        catch (UnauthorizedAccessException) { }
    }
}
