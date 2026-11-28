using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace GtaVietLife.Launcher.Services;

/// <summary>A community MLO pack declared by its original link and SHA-256; the Launcher never repackages it.</summary>
public sealed class MloPack {
 public string Name {get;set;}="";
 public string Url {get;set;}="";
 public string Sha256 {get;set;}="";
 public string DlcName {get;set;}="";
}

public sealed record MloResult(string Name,bool Installed,string Message);

public static class MloPacks {
 static readonly JsonSerializerOptions Options=new(){PropertyNameCaseInsensitive=true};

 /// <summary>Packs from the "mlo_packs" list of manifest.json and from the user's own mlo-packs.json.</summary>
 public static List<MloPack> Load(string manifestPath,string userPath){
  var packs=new List<MloPack>();
  if(File.Exists(manifestPath)){
   using var document=JsonDocument.Parse(File.ReadAllText(manifestPath));
   if(document.RootElement.TryGetProperty("mlo_packs",out var list)&&list.ValueKind==JsonValueKind.Array)
    packs.AddRange(list.Deserialize<List<MloPack>>(Options)??new());
  }
  if(File.Exists(userPath))packs.AddRange(JsonSerializer.Deserialize<List<MloPack>>(File.ReadAllText(userPath),Options)??new());
  return packs.GroupBy(p=>p.DlcName,StringComparer.OrdinalIgnoreCase).Select(g=>g.Last()).ToList();
 }

 public static string? Problem(MloPack pack){
  if(string.IsNullOrWhiteSpace(pack.Name))return "thiếu tên";
  if(!Uri.TryCreate(pack.Url,UriKind.Absolute,out var uri)||uri.Scheme!=Uri.UriSchemeHttps)return "link phải là https";
  if(pack.Sha256.Length!=64||!pack.Sha256.All(Uri.IsHexDigit))return "thiếu SHA-256 hợp lệ";
  if(pack.DlcName.Length is 0 or >40||!pack.DlcName.All(c=>char.IsLetterOrDigit(c)||c=='_'))return "dlcName chỉ gồm chữ, số, gạch dưới";
  return null;
 }

 public static string Target(string gameDir,MloPack pack)=>Path.Combine(gameDir,"mods","update","x64","dlcpacks",pack.DlcName,"dlc.rpf");

 /// <summary>Downloads into staging, checks the hash, then puts dlc.rpf into mods/dlcpacks with a backup of any older copy.</summary>
 public static async Task<List<MloResult>> InstallAll(IEnumerable<MloPack> packs,string gameDir,string staging,string backups,
   Func<string,string,CancellationToken,Task> download,CancellationToken ct){
  var results=new List<MloResult>();
  Directory.CreateDirectory(staging);
  var record=Path.Combine(staging,"installed.json");
  var installed=File.Exists(record)?JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(record))??new():new Dictionary<string,string>();
  foreach(var pack in packs){
   var problem=Problem(pack);
   if(problem!=null){results.Add(new(pack.Name,false,"Bỏ qua: "+problem));continue;}
   var target=Target(gameDir,pack);
   if(installed.TryGetValue(pack.DlcName,out var have)&&have.Equals(pack.Sha256,StringComparison.OrdinalIgnoreCase)&&File.Exists(target)){
    results.Add(new(pack.Name,true,"Đã có"));continue;
   }
   var archive=Path.Combine(staging,pack.DlcName+".download");
   try{
    await download(pack.Url,archive,ct);
    string actual;
    using(var stream=File.OpenRead(archive))actual=Convert.ToHexString(await SHA256.HashDataAsync(stream,ct)).ToLowerInvariant();
    if(!actual.Equals(pack.Sha256,StringComparison.OrdinalIgnoreCase)){results.Add(new(pack.Name,false,"Sai SHA-256, không cài"));continue;}
    var rpf=Path.Combine(staging,pack.DlcName+".rpf");
    if(!ExtractRpf(archive,rpf)){results.Add(new(pack.Name,false,"Gói không có dlc.rpf (chỉ hỗ trợ .zip hoặc .rpf)"));continue;}
    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
    if(File.Exists(target)){
     var keep=Path.Combine(backups,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+pack.DlcName);
     Directory.CreateDirectory(keep);File.Copy(target,Path.Combine(keep,"dlc.rpf"),true);
    }
    var temp=target+".tmp";File.Copy(rpf,temp,true);File.Move(temp,target,true);
    installed[pack.DlcName]=pack.Sha256.ToLowerInvariant();
    File.WriteAllText(record,JsonSerializer.Serialize(installed));
    results.Add(new(pack.Name,true,"Đã cài vào mods\\update\\x64\\dlcpacks\\"+pack.DlcName+". Cần có dòng dlcpacks:/"+pack.DlcName+"/ trong dlclist.xml (DLCForge hoặc Simple Mods Loader làm được)."));
   }catch(Exception ex)when(ex is IOException or HttpRequestException or InvalidDataException or UnauthorizedAccessException){
    results.Add(new(pack.Name,false,"Lỗi: "+ex.Message));
   }finally{
    try{if(File.Exists(archive))File.Delete(archive);}catch(IOException){}
   }
  }
  return results;
 }

 /// <summary>Accepts a bare dlc.rpf or a zip that contains one.</summary>
 public static bool ExtractRpf(string archive,string output){
  using(var head=File.OpenRead(archive)){
   var magic=new byte[4];
   if(head.Read(magic,0,4)==4&&magic[0]==0x37&&magic[1]==0x46&&magic[2]==0x50&&magic[3]==0x52){head.Close();File.Copy(archive,output,true);return true;}
  }
  try{
   using var zip=ZipFile.OpenRead(archive);
   var entry=zip.Entries.FirstOrDefault(e=>e.Name.Equals("dlc.rpf",StringComparison.OrdinalIgnoreCase));
   if(entry==null)return false;
   entry.ExtractToFile(output,true);return true;
  }catch(InvalidDataException){return false;}
 }
}
