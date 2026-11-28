using System.Diagnostics;
using System.IO.Compression;
if(args.Length<3){Console.Error.WriteLine("Usage: updater package.zip install-dir parent-pid");return 2;}
var zip=Path.GetFullPath(args[0]);var target=Path.GetFullPath(args[1]);if(!int.TryParse(args[2],out var pid))return 2;
try{var parentProcess=Process.GetProcessById(pid);if(!parentProcess.WaitForExit(30000))throw new TimeoutException("Launcher did not exit before update timeout.");}catch(ArgumentException){}
var parent=Path.GetDirectoryName(target)!;var stage=Path.Combine(parent,"gvl-stage-"+Guid.NewGuid().ToString("N"));var backup=Path.Combine(parent,"gvl-backup-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(stage);
bool oldVersionMoved=false;
var marker=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GtaVietLife","update-pending");
try{
 ZipFile.ExtractToDirectory(zip,stage,true);
 if(!File.Exists(Path.Combine(stage,"GtaVietLife.Launcher.exe")))throw new InvalidDataException("Package missing Launcher executable.");
 if(Directory.Exists(target)){Directory.Move(target,backup);oldVersionMoved=true;}
 Directory.Move(stage,target);
 // Keep the installer's uninstall data, which the update package does not contain.
 if(oldVersionMoved)foreach(var file in Directory.GetFiles(backup,"unins*"))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
 Directory.CreateDirectory(Path.GetDirectoryName(marker)!);File.WriteAllText(marker,backup);
 var started=Process.Start(new ProcessStartInfo(Path.Combine(target,"GtaVietLife.Launcher.exe")){UseShellExecute=true,WorkingDirectory=target})
  ??throw new InvalidOperationException("New Launcher did not start.");
 // The new Launcher deletes the marker once its self-check passes; otherwise the previous version comes back.
 var deadline=DateTime.UtcNow.AddSeconds(int.TryParse(Environment.GetEnvironmentVariable("GVL_UPDATE_HEALTH_SECONDS"),out var s)?s:120);
 while(File.Exists(marker)&&!started.HasExited&&DateTime.UtcNow<deadline)Thread.Sleep(500);
 if(File.Exists(marker)){
  try{if(!started.HasExited){started.Kill(true);started.WaitForExit(10000);}}catch(InvalidOperationException){}
  File.Delete(marker);
  throw new InvalidOperationException("New version failed its startup check.");
 }
 Console.WriteLine("Update installed; previous version retained at "+backup);return 0;
}catch(Exception ex){
 Console.Error.WriteLine(ex);
 try{if(File.Exists(marker))File.Delete(marker);}catch(IOException locked){Console.Error.WriteLine(locked.Message);}
 try{
  if(oldVersionMoved&&Directory.Exists(backup)){
   if(Directory.Exists(target))Directory.Delete(target,true);
   Directory.Move(backup,target);
   var previous=Path.Combine(target,"GtaVietLife.Launcher.exe");
   if(File.Exists(previous))try{Process.Start(new ProcessStartInfo(previous){UseShellExecute=true,WorkingDirectory=target});}catch(Exception restart){Console.Error.WriteLine(restart.Message);}
  }
 }catch(Exception rollback){Console.Error.WriteLine("ROLLBACK FAILED: "+rollback);return 3;}
 try{if(Directory.Exists(stage))Directory.Delete(stage,true);}catch{}
 return 1;
}
