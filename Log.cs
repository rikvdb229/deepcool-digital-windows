namespace DeepCoolDisplay;

/// <summary>
/// Connect/disconnect events only, never per-update lines. The file is capped
/// so it cannot grow the way DeepCool's service.log did.
/// </summary>
static class Log
{
    const long MaxBytes = 256 * 1024;
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "DeepCoolDisplay", "events.log");

    public static void Info(string message) => Write("INFO ", message);
    public static void Error(string message) => Write("ERROR", message);

    static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {level} {message}";
        Console.WriteLine(line);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var file = new FileInfo(FilePath);
            if (file.Exists && file.Length > MaxBytes)
                file.Delete();
            File.AppendAllText(FilePath, line + Environment.NewLine);
        }
        catch
        {
            // Logging must never stop the display loop.
        }
    }
}
