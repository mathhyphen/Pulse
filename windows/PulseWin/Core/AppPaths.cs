namespace PulseWin.Core;

/// <summary>
/// Where PulseWin keeps its own files.
///
/// Pulse uses <c>~/Library/Application Support/Pulse</c>. The Windows equivalent
/// is <c>%APPDATA%\PulseWin</c>, which roams with the user profile — the same
/// intent. Nothing here is a provider's directory; those are read in place and
/// never written to.
/// </summary>
public static class AppPaths
{
    public static string Directory { get; } = EnsureDirectory(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PulseWin"));

    /// <summary>Manually entered keys, encrypted at rest. The counterpart of Pulse's <c>keys.dat</c>.</summary>
    public static string Secrets => Path.Combine(Directory, "keys.dat");

    public static string Settings => Path.Combine(Directory, "settings.json");

    /// <summary>One mark per currency — see <see cref="DeepSeekMarks"/>.</summary>
    public static string DeepSeekBaseline => Path.Combine(Directory, "deepseek-baseline.json");

    /// <summary>The last good reading per account, so a restart is not a blank rail.</summary>
    public static string Cache => Path.Combine(Directory, "cache.json");

    /// <summary>The reader's home directory, which is where the CLIs keep their logins.</summary>
    public static string Home { get; } =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string EnsureDirectory(string path)
    {
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// Writes a file atomically, so a crash mid-write cannot leave a truncated
    /// settings file behind — the same reason Pulse writes its marks atomically.
    /// </summary>
    public static void WriteAtomic(string path, string contents)
    {
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true);
    }
}
