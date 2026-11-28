# A new version that never confirms its startup check must be rolled back; a confirming one is kept.
param([Parameter(Mandatory=$true)][string]$UpdaterPath)
$ErrorActionPreference="Stop"
$updater=(Resolve-Path $UpdaterPath).Path
$csc=Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$marker=Join-Path $env:LOCALAPPDATA "GtaVietLife\update-pending"
if(Test-Path $marker){throw "A real update is pending on this machine; not running the test."}
$root=Join-Path $env:TEMP ("gvl-health-test-"+[Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $root | Out-Null
function New-Package([string]$Name,[string]$Body) {
  $dir=Join-Path $root $Name; New-Item -ItemType Directory -Force $dir | Out-Null
  $src=Join-Path $root "$Name.cs"
  Set-Content -Path $src -Value ('using System; using System.IO; static class P { static void Main() { ' + $Body + ' } }')
  & $csc /nologo /target:winexe ("/out:" + (Join-Path $dir "GtaVietLife.Launcher.exe")) $src | Out-Null
  if($LASTEXITCODE -ne 0){throw "Could not compile test launcher $Name"}
  $zip=Join-Path $root "$Name.zip"; Compress-Archive -Path (Join-Path $dir "*") -DestinationPath $zip -Force; return $zip
}
try {
  $env:GVL_UPDATE_HEALTH_SECONDS="6"
  $confirm='File.Delete(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"GtaVietLife\update-pending"));'
  $cases=@(
    @{Name="crash"; Body='Environment.Exit(7);'; Ok=$false},
    @{Name="hang"; Body='System.Threading.Thread.Sleep(60000);'; Ok=$false},
    @{Name="healthy"; Body=$confirm; Ok=$true})
  foreach($case in $cases){
    $target=Join-Path $root ("install-"+$case.Name); New-Item -ItemType Directory -Force $target | Out-Null
    Set-Content (Join-Path $target "old.marker") "known-good"; Set-Content (Join-Path $target "unins000.dat") "uninstall"
    $zip=New-Package $case.Name $case.Body
    $p=Start-Process -FilePath $updater -ArgumentList @("`"$zip`"","`"$target`"","2147483647") -Wait -PassThru -WindowStyle Hidden
    if($case.Ok){
      if($p.ExitCode -ne 0){throw "$($case.Name): healthy update rejected ($($p.ExitCode))"}
      if(Test-Path (Join-Path $target "old.marker")){throw "$($case.Name): old files still active"}
      if(!(Test-Path (Join-Path $target "unins000.dat"))){throw "$($case.Name): uninstall data lost"}
    } else {
      if($p.ExitCode -eq 0){throw "$($case.Name): broken update accepted"}
      if(!(Test-Path (Join-Path $target "old.marker"))){throw "$($case.Name): previous version not restored"}
    }
    if(Test-Path $marker){throw "$($case.Name): pending marker left behind"}
    Write-Host "$($case.Name): ok"
  }
  Write-Host "Updater health tests passed."
}
finally {
  Remove-Item Env:GVL_UPDATE_HEALTH_SECONDS -ErrorAction SilentlyContinue
  Start-Sleep -Milliseconds 500
  Remove-Item $root -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
