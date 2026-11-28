using Microsoft.Win32;

namespace GtaVietLife.Launcher.Services;

/// <summary>Reads where stores installed the game; <see cref="GameLocator"/> checks the candidates.</summary>
public static class InstalledGame
{
    public static string? Find()
    {
        try { return GameLocator.FirstGame(Candidates()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            RuntimeManager.Log("Không dò được thư mục game: " + ex.Message);
            return null;
        }
    }

    static IEnumerable<string?> Candidates()
    {
        yield return Read(@"SOFTWARE\WOW6432Node\Rockstar Games\Grand Theft Auto V", "InstallFolder");
        yield return Read(@"SOFTWARE\WOW6432Node\Rockstar Games\GTAV", "InstallFolderSteam");
        var steam = Read(@"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath");
        if (steam == null) yield break;
        var libraries = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(libraries)) yield break;
        foreach (var folder in GameLocator.SteamGameFolders(File.ReadAllText(libraries))) yield return folder;
    }

    static string? Read(string key, string value)
    {
        using var opened = Registry.LocalMachine.OpenSubKey(key);
        return opened?.GetValue(value) as string;
    }
}
