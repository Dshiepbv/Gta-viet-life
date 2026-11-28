using System.Security.Cryptography;
using System.Text.Json;
namespace GtaVietLife.Launcher.Services;

/// <summary>manifest.json lists every shipped file with its SHA-256 so a damaged installation is detected at startup.</summary>
public sealed class PackageManifest {
 public const string FileName="manifest.json";
 public string Version {get;set;}="";
 public Dictionary<string,string> Files {get;set;}=new();

 public static PackageManifest? Load(string root){
  var path=Path.Combine(root,FileName);if(!File.Exists(path))return null;
  var options=new JsonSerializerOptions{PropertyNameCaseInsensitive=true};
  return JsonSerializer.Deserialize<PackageManifest>(File.ReadAllText(path),options)??throw new InvalidDataException("manifest.json rỗng.");
 }

 public static string HashFile(string path){
  using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
 }

 /// <summary>Returns one line per missing, altered or unsafe entry. Empty means the package is intact.</summary>
 public List<string> Verify(string root){
  var problems=new List<string>();
  var full=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(Files.Count==0)problems.Add("manifest.json không liệt kê file nào");
  foreach(var entry in Files){
   var path=Path.GetFullPath(Path.Combine(root,entry.Key));
   if(!path.StartsWith(full,StringComparison.OrdinalIgnoreCase)){problems.Add("đường dẫn không hợp lệ: "+entry.Key);continue;}
   if(!File.Exists(path)){problems.Add("thiếu "+entry.Key);continue;}
   if(!HashFile(path).Equals(entry.Value,StringComparison.OrdinalIgnoreCase))problems.Add("sai checksum "+entry.Key);
  }
  return problems;
 }
}
