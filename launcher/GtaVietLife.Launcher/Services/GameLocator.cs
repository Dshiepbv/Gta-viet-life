using System.Text.RegularExpressions;

namespace GtaVietLife.Launcher.Services;

/// <summary>Finds an installed GTA V Legacy so the player does not have to browse for it.</summary>
public static class GameLocator
{
    const string Executable = "GTA5.exe";

    /// <summary>First candidate folder that holds the Legacy executable, or null.</summary>
    public static string? FirstGame(IEnumerable<string?> folders)
    {
        foreach (var folder in folders)
        {
            if (string.IsNullOrWhiteSpace(folder)) continue;
            string path;
            try { path = Path.Combine(folder.Trim().Trim('"'), Executable); }
            catch (ArgumentException) { continue; }
            if (File.Exists(path)) return path;
        }
        return null;
    }

    /// <summary>Library folders listed in Steam's libraryfolders.vdf.</summary>
    public static IEnumerable<string> SteamGameFolders(string libraryFoldersText)
    {
        foreach (Match match in Regex.Matches(libraryFoldersText, "\"path\"\\s+\"((?:[^\"\\\\]|\\\\.)*)\""))
            yield return Path.Combine(match.Groups[1].Value.Replace("\\\\", "\\"),
                "steamapps", "common", "Grand Theft Auto V");
    }
}
