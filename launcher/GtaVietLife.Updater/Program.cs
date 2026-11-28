using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;

// Chay an (WinExe): moi buoc ghi vao %LOCALAPPDATA%\GtaVietLife\updater.log thay vi cua so console.
var logDir=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"GtaVietLife");
Directory.CreateDirectory(logDir);
var logFile=Path.Combine(logDir,"updater.log");
void Log(string text){try{File.AppendAllText(logFile,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+text+Environment.NewLine);}catch(IOException){}}

if(args.Length<3){
 Log("Started without arguments; nothing to do.");
 if(Environment.GetEnvironmentVariable("GVL_UPDATER_QUIET")!="1")
  Native.MessageBoxW(IntPtr.Zero,"Đây là trình cập nhật của GTA Viet Life, không mở trực tiếp.\n\nHãy mở \"GTA Viet Life Launcher\" rồi bấm nút Cập nhật.","GTA Viet Life",0x40);
 return 2;
}
var zip=Path.GetFullPath(args[0]);var target=Path.GetFullPath(args[1]);if(!int.TryParse(args[2],out var pid))return 2;
// Windows khong cho doi ten thu muc dang la thu muc lam viec cua mot tien trinh: ra khoi thu muc cai truoc.
Environment.CurrentDirectory=Path.GetTempPath();
Log("Update requested: package="+zip+" target="+target+" launcher pid="+pid);
try{var parentProcess=Process.GetProcessById(pid);if(!parentProcess.WaitForExit(30000))throw new TimeoutException("Launcher did not exit before update timeout.");}catch(ArgumentException){}
var parent=Path.GetDirectoryName(target)!;var stage=Path.Combine(parent,"gvl-stage-"+Guid.NewGuid().ToString("N"));var backup=Path.Combine(parent,"gvl-backup-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N"));
bool oldVersionMoved=false;
var marker=Path.Combine(logDir,"update-pending");
try{
 Directory.CreateDirectory(stage);
 ZipFile.ExtractToDirectory(zip,stage,true);
 if(!File.Exists(Path.Combine(stage,"GtaVietLife.Launcher.exe")))throw new InvalidDataException("Package missing Launcher executable.");
 // Exe hong thi Windows hien hop thoai chan; kiem dau file PE truoc khi thay the.
 if(!LooksLikeWindowsExe(Path.Combine(stage,"GtaVietLife.Launcher.exe")))throw new InvalidDataException("Launcher in the package is not a Windows executable.");
 if(Directory.Exists(target)){
  StopProcessesIn(target);
  MoveWithRetry(target,backup);oldVersionMoved=true;
 }
 MoveWithRetry(stage,target);
 // Keep the installer's uninstall data, which the update package does not contain.
 if(oldVersionMoved)foreach(var file in Directory.GetFiles(backup,"unins*"))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
 File.WriteAllText(marker,backup);
 var started=Process.Start(new ProcessStartInfo(Path.Combine(target,"GtaVietLife.Launcher.exe")){UseShellExecute=false,WorkingDirectory=target})
  ??throw new InvalidOperationException("New Launcher did not start.");
 // The new Launcher deletes the marker once its self-check passes; otherwise the previous version comes back.
 var deadline=DateTime.UtcNow.AddSeconds(int.TryParse(Environment.GetEnvironmentVariable("GVL_UPDATE_HEALTH_SECONDS"),out var s)?s:120);
 while(File.Exists(marker)&&!started.HasExited&&DateTime.UtcNow<deadline)Thread.Sleep(500);
 if(File.Exists(marker)){
  try{if(!started.HasExited){started.Kill(true);started.WaitForExit(10000);}}catch(InvalidOperationException){}
  File.Delete(marker);
  throw new InvalidOperationException("New version failed its startup check.");
 }
 Log("Update installed; previous version retained at "+backup);return 0;
}catch(Exception ex){
 Log("Update failed: "+ex);
 try{if(File.Exists(marker))File.Delete(marker);}catch(IOException locked){Log(locked.Message);}
 try{
  if(oldVersionMoved&&Directory.Exists(backup)){
   if(Directory.Exists(target)){StopProcessesIn(target);Directory.Delete(target,true);}
   MoveWithRetry(backup,target);
   Log("Previous version restored.");
  }
 }catch(Exception rollback){Log("ROLLBACK FAILED: "+rollback);return 3;}
 try{if(Directory.Exists(stage))Directory.Delete(stage,true);}catch(Exception cleanup){Log("Stage cleanup: "+cleanup.Message);}
 // Mo lai Launcher dang co de nguoi dung thay loi thay vi khong co gi.
 var previous=Path.Combine(target,"GtaVietLife.Launcher.exe");
 if(File.Exists(previous)&&Environment.GetEnvironmentVariable("GVL_UPDATER_QUIET")!="1")
  try{Process.Start(new ProcessStartInfo(previous){UseShellExecute=true,WorkingDirectory=target});}catch(Exception restart){Log(restart.Message);}
 return 1;
}

// Dich vu cua mod con chay (Engine, Bridge) giu file trong thu muc cai: dung chung truoc khi doi thu muc.
static void StopProcessesIn(string folder){
 var prefix=folder.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
 foreach(var name in new[]{"GtaVietLifeEngine","GtaVietLifeBridge","GtaVietLife.Launcher"})
  foreach(var p in Process.GetProcessesByName(name)){
   try{if(p.MainModule?.FileName?.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)==true){p.Kill(true);p.WaitForExit(5000);}}
   catch(Exception){}finally{p.Dispose();}
  }
}

static bool LooksLikeWindowsExe(string path){
 try{
  using var stream=File.OpenRead(path);using var reader=new BinaryReader(stream);
  if(stream.Length<0x40||reader.ReadUInt16()!=0x5A4D)return false;      // "MZ"
  stream.Position=0x3C;var pe=reader.ReadInt32();
  if(pe<=0||pe>stream.Length-4)return false;
  stream.Position=pe;return reader.ReadUInt32()==0x00004550;             // "PE\0\0"
 }catch(IOException){return false;}
}

// File vua dong (antivirus, Explorer, tien trinh vua tat) co the con khoa vai giay.
static void MoveWithRetry(string from,string to){
 for(var attempt=1;;attempt++){
  try{Directory.Move(from,to);return;}
  catch(Exception ex)when((ex is IOException or UnauthorizedAccessException)&&attempt<20){Thread.Sleep(500);}
 }
}

static class Native{
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]
 public static extern int MessageBoxW(IntPtr hWnd,string text,string caption,uint type);
}
