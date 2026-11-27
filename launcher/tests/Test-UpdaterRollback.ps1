param([Parameter(Mandatory=$true)][string]$UpdaterPath)
$ErrorActionPreference="Stop"
$updater=(Resolve-Path $UpdaterPath).Path
$root=Join-Path $env:TEMP ("gvl-updater-test-"+[Guid]::NewGuid().ToString("N"))
$target=Join-Path $root "install"
New-Item -ItemType Directory -Force $target | Out-Null
Set-Content -Path (Join-Path $target "old.marker") -Value "known-good"
try {
  $bad=Join-Path $root "bad.zip"
  Set-Content -Path $bad -Value "not a zip"
  & $updater $bad $target "2147483647"
  if($LASTEXITCODE -eq 0){throw "Updater unexpectedly accepted an invalid archive."}
  if(!(Test-Path (Join-Path $target "old.marker"))){throw "Invalid archive damaged the current installation."}

  $payload=Join-Path $root "payload"
  New-Item -ItemType Directory -Force $payload | Out-Null
  Set-Content -Path (Join-Path $payload "GtaVietLife.Launcher.exe") -Value "intentionally invalid executable for rollback test"
  $goodShape=Join-Path $root "valid-shape.zip"
  Compress-Archive -Path (Join-Path $payload "*") -DestinationPath $goodShape -Force
  & $updater $goodShape $target "2147483647"
  if($LASTEXITCODE -eq 0){throw "Updater unexpectedly launched the intentionally invalid test executable."}
  if(!(Test-Path (Join-Path $target "old.marker"))){throw "Failed update did not restore the last known-good installation."}
  Write-Host "Updater rollback tests passed."
}
finally {
  if(Test-Path $root){Remove-Item $root -Recurse -Force}
}
