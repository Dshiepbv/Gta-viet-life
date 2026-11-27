using System.Diagnostics;
using System.IO.Compression;
if(args.Length<3){Console.Error.WriteLine("Usage: updater package.zip install-dir parent-pid");return 2;}
var zip=Path.GetFullPath(args[0]);var target=Path.GetFullPath(args[1]);if(!int.TryParse(args[2],out var pid))return 2;
try{var parentProcess=Process.GetProcessById(pid);if(!parentProcess.WaitForExit(30000))throw new TimeoutException("Launcher did not exit before update timeout.");}catch(ArgumentException){}
var parent=Path.GetDirectoryName(target)!;var stage=Path.Combine(parent,"gvl-stage-"+Guid.NewGuid().ToString("N"));var backup=Path.Combine(parent,"gvl-backup-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(stage);
bool oldVersionMoved=false;
try{
 ZipFile.ExtractToDirectory(zip,stage,true);
 if(!File.Exists(Path.Combine(stage,"GtaVietLife.Launcher.exe")))throw new InvalidDataException("Package missing Launcher executable.");
 if(Directory.Exists(target)){Directory.Move(target,backup);oldVersionMoved=true;}
 Directory.Move(stage,target);
 Process.Start(new ProcessStartInfo(Path.Combine(target,"GtaVietLife.Launcher.exe")){UseShellExecute=true,WorkingDirectory=target});
 Console.WriteLine("Update installed; previous version retained at "+backup);return 0;
}catch(Exception ex){
 Console.Error.WriteLine(ex);
 try{
  if(oldVersionMoved&&Directory.Exists(backup)){
   if(Directory.Exists(target))Directory.Delete(target,true);
   Directory.Move(backup,target);
  }
 }catch(Exception rollback){Console.Error.WriteLine("ROLLBACK FAILED: "+rollback);return 3;}
 try{if(Directory.Exists(stage))Directory.Delete(stage,true);}catch{}
 return 1;
}
