using GtaVietLife.Launcher.Models;
namespace GtaVietLife.Launcher.Services;

/// <summary>The updater creates the marker; deleting it tells the updater the new version works.</summary>
public static class UpdateHealth {
 public static string Marker=>Path.Combine(LauncherSettings.Dir,"update-pending");
 public static bool Pending=>File.Exists(Marker);
 public static void Confirm(){if(File.Exists(Marker))File.Delete(Marker);}
}
