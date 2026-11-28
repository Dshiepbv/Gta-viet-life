namespace GtaVietLife.Launcher.Services;

/// <summary>Checks a GTA V Legacy folder for the script hooks the mod needs. Nothing here writes to disk.</summary>
public static class LegacyEnvironment {
 /// <summary>Builds from 1.0.3258.0 need a SHVDN nightly (v3.6.0-nightly.89 or later) according to the SHVDN README.</summary>
 public const int FirstBuildNeedingNightly=3258;

 public static int GameBuild(string fileVersion){
  var parts=(fileVersion??"").Replace(',','.').Split('.');
  return parts.Length>=3&&int.TryParse(parts[2].Trim(),out var build)?build:0;
 }

 /// <returns>Null when everything required is present, otherwise a message for the player.</returns>
 public static string? FindProblem(string gameExe,string fileVersion){
  var name=Path.GetFileName(gameExe);
  if(name.Contains("Enhanced",StringComparison.OrdinalIgnoreCase))
   return "Bản này dành cho GTA V Legacy (GTA5.exe). Bạn đang chọn bản Enhanced.";
  if(!name.Equals("GTA5.exe",StringComparison.OrdinalIgnoreCase))return "Hãy chọn đúng tệp GTA5.exe trong thư mục game.";
  var dir=Path.GetDirectoryName(Path.GetFullPath(gameExe))!;
  bool Has(string file)=>File.Exists(Path.Combine(dir,file));
  var missing=new List<string>();
  if(!Has("ScriptHookV.dll"))missing.Add("ScriptHookV.dll (Script Hook V)");
  if(!Has("dinput8.dll")&&!Has("xinput1_4.dll")&&!Has("version.dll"))missing.Add("dinput8.dll (ASI loader đi kèm Script Hook V)");
  bool nightly=Has("ScriptHookVDotNet-Loader.asi")&&Has("ScriptHookVDotNet.dll");
  bool stable=Has("ScriptHookVDotNet.asi");
  if(!nightly&&!stable)missing.Add("ScriptHookVDotNet (bộ .asi và .dll)");
  if(!Has("ScriptHookVDotNet3.dll"))missing.Add("ScriptHookVDotNet3.dll");
  if(missing.Count>0)return "Thiếu thành phần trong thư mục game: "+string.Join(", ",missing)+".";
  if(stable&&!nightly&&GameBuild(fileVersion)>=FirstBuildNeedingNightly)
   return "GTA build "+fileVersion+" cần ScriptHookVDotNet bản nightly 3.6.0-nightly.89 trở lên; bản ổn định 3.6.0 không tương thích.";
  return null;
 }
}

/// <summary>
/// Installs the compiled script assemblies into the game's scripts folder as one transaction.
/// Player saves are never touched, existing .ini settings are kept, and replaced files are backed up.
/// </summary>
public static class ScriptInstaller {
 public sealed record Result(int Copied,int Retired,string? BackupDir);

 static bool Newer(string candidate,string installed){
  Version Of(string path){return Version.TryParse(System.Diagnostics.FileVersionInfo.GetVersionInfo(path).FileVersion,out var v)?v:new Version(0,0);}
  return Of(candidate)>Of(installed);
 }

 public static Result Install(string source,string scripts,string backupRoot,Action<string>? beforeEachWrite=null){
  var assemblies=Directory.Exists(source)?Directory.GetFiles(source,"GtaVietLife*.dll"):Array.Empty<string>();
  if(assemblies.Length==0)throw new FileNotFoundException("Bộ cài thiếu module game (GtaVietLife*.dll).",source);
  var plan=new List<(string From,string To)>();
  foreach(var file in assemblies)plan.Add((file,Path.Combine(scripts,Path.GetFileName(file))));
  // Thu vien menu (MIT) ma module cua hang can.
  var lemon=Path.Combine(source,"LemonUI.SHVDN3.dll");
  var lemonTarget=Path.Combine(scripts,"LemonUI.SHVDN3.dll");
  // Mod khac co the da dung ban LemonUI moi hon: khong ha cap.
  if(File.Exists(lemon)&&(!File.Exists(lemonTarget)||Newer(lemon,lemonTarget)))plan.Add((lemon,lemonTarget));
  foreach(var file in Directory.GetFiles(source,"GtaVietLife*.ini")){
   var target=Path.Combine(scripts,Path.GetFileName(file));if(!File.Exists(target))plan.Add((file,target));
  }
  foreach(var folderName in new[]{"GtaVietLifeIcons","GtaVietLifeData"}){
   var folder=Path.Combine(source,folderName);
   if(Directory.Exists(folder))foreach(var file in Directory.GetFiles(folder,"*",SearchOption.AllDirectories))
    plan.Add((file,Path.Combine(scripts,folderName,Path.GetRelativePath(folder,file))));
  }
  plan=plan.Where(p=>!File.Exists(p.To)||PackageManifest.HashFile(p.From)!=PackageManifest.HashFile(p.To)).ToList();
  // Raw sources from earlier versions would load a second copy of every script.
  var retired=Directory.Exists(scripts)?Directory.GetFiles(scripts,"GtaVietLife*.cs"):Array.Empty<string>();
  if(plan.Count==0&&retired.Length==0)return new Result(0,0,null);

  var backup=Path.Combine(backupRoot,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));
  var undo=new Stack<Action>();
  try{
   Directory.CreateDirectory(scripts);Directory.CreateDirectory(backup);
   foreach(var old in retired){
    var saved=Path.Combine(backup,Path.GetFileName(old));beforeEachWrite?.Invoke(old);
    File.Copy(old,saved,true);File.Delete(old);undo.Push(()=>File.Copy(saved,old,true));
   }
   foreach(var (from,to) in plan){
    beforeEachWrite?.Invoke(to);
    Directory.CreateDirectory(Path.GetDirectoryName(to)!);
    var temp=to+".gvltmp";File.Copy(from,temp,true);
    if(File.Exists(to)){
     var saved=Path.Combine(backup,Path.GetRelativePath(scripts,to));Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
     File.Copy(to,saved,true);File.Move(temp,to,true);undo.Push(()=>File.Copy(saved,to,true));
    }else{File.Move(temp,to);undo.Push(()=>File.Delete(to));}
   }
   return new Result(plan.Count,retired.Length,backup);
  }catch{
   foreach(var step in undo){try{step();}catch(Exception ex){RuntimeLog?.Invoke("Hoàn nguyên lỗi: "+ex.Message);}}
   foreach(var (_,to) in plan){try{File.Delete(to+".gvltmp");}catch(IOException){}}
   throw;
  }
 }
 public static Action<string>? RuntimeLog;
}
