using System.Text.Json;
namespace GtaVietLife.Launcher.Models;
public sealed class LauncherSettings {
 public string GameExecutable {get;set;}="";
 public bool AutoLaunchGame {get;set;}
 public static string Dir=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GtaVietLife");
 public static string FileName=>Path.Combine(Dir,"config.json");
 static string LegacyFileName=>Path.Combine(Dir,"launcher.json");
 public static LauncherSettings Load(){Directory.CreateDirectory(Dir);try{var source=File.Exists(FileName)?FileName:LegacyFileName;return File.Exists(source)?JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(source))??new():new();}catch{return new();}}
 public void Save(){Directory.CreateDirectory(Dir);var t=FileName+".tmp";File.WriteAllText(t,JsonSerializer.Serialize(this,new JsonSerializerOptions{WriteIndented=true}));File.Move(t,FileName,true);}
}