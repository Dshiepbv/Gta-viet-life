param([Parameter(Mandatory=$true)][string]$UpdaterPath)
$ErrorActionPreference="Stop"
$updater=(Resolve-Path $UpdaterPath).Path
$root=Join-Path $env:TEMP ("gvl-updater-test-"+[Guid]::NewGuid().ToString("N"))
$target=Join-Path $root "install"
New-Item -ItemType Directory -Force $target | Out-Null
Set-Content -Path (Join-Path $target "old.marker") -Value "known-good"
# The updater is a windowed app: wait for it explicitly and read its exit code.
function Invoke-Updater([string[]]$Arguments) {
  $quoted = $Arguments | ForEach-Object { "`"$_`"" }
  if ($quoted) { $p = Start-Process -FilePath $updater -ArgumentList $quoted -Wait -PassThru }
  else { $p = Start-Process -FilePath $updater -Wait -PassThru }
  return $p.ExitCode
}
$env:GVL_UPDATER_QUIET = "1"
try {
  if ((Invoke-Updater @()) -ne 2) { throw "Updater without arguments must stop with code 2." }

  $bad=Join-Path $root "bad.zip"
  Set-Content -Path $bad -Value "not a zip"
  if ((Invoke-Updater @($bad, $target, "2147483647")) -eq 0) { throw "Updater unexpectedly accepted an invalid archive." }
  if(!(Test-Path (Join-Path $target "old.marker"))){throw "Invalid archive damaged the current installation."}

  $payload=Join-Path $root "payload"
  New-Item -ItemType Directory -Force $payload | Out-Null
  Set-Content -Path (Join-Path $payload "GtaVietLife.Launcher.exe") -Value "intentionally invalid executable for rollback test"
  $goodShape=Join-Path $root "valid-shape.zip"
  Compress-Archive -Path (Join-Path $payload "*") -DestinationPath $goodShape -Force
  if ((Invoke-Updater @($goodShape, $target, "2147483647")) -eq 0) { throw "Updater unexpectedly launched the intentionally invalid test executable." }
  if(!(Test-Path (Join-Path $target "old.marker"))){throw "Failed update did not restore the last known-good installation."}
  if(Test-Path (Join-Path $env:LOCALAPPDATA "GtaVietLife\update-pending")){throw "Failed update left the pending marker behind."}
  Write-Host "Updater rollback tests passed."
}
finally {
  Remove-Item Env:GVL_UPDATER_QUIET -ErrorAction SilentlyContinue
  if(Test-Path $root){Remove-Item $root -Recurse -Force}
}
exit 0
