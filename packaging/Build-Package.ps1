# Dung toan bo bo cai: kiem thu, bien dich module game, Launcher, Updater, Engine Host, Bridge,
# manifest.json, goi zip cap nhat va file cai dat duy nhat. Dung chung cho may local va CI.
param(
  [string]$Version = "0.4.0",
  [string]$Dotnet = "dotnet",
  [string]$Python = "python",
  [Parameter(Mandatory = $true)][string]$InnoCompiler,
  [Parameter(Mandatory = $true)][string]$WorkDir
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root
New-Item -ItemType Directory -Force $WorkDir | Out-Null
# Dan dung trong thu muc moi ngoai cay ma nguon: trinh theo doi file cua IDE hay giu file va chan viec xoa.
$publish = Join-Path $WorkDir ("publish-" + [Guid]::NewGuid().ToString("N").Substring(0, 8))
$artifacts = Join-Path $root "artifacts"
New-Item -ItemType Directory -Force $publish, $artifacts | Out-Null
Get-ChildItem $artifacts -File | Remove-Item -Force
function Invoke-Step([string]$Name, [scriptblock]$Action) {
  Write-Host "=== $Name"
  # Cong cu native ghi canh bao ra stderr; chi ma thoat moi quyet dinh that bai.
  $previous = $ErrorActionPreference; $script:ErrorActionPreference = "Continue"
  $global:LASTEXITCODE = 0
  & $Action
  $code = $LASTEXITCODE; $script:ErrorActionPreference = $previous
  if ($code -ne 0) { throw "$Name failed with exit code $code" }
}

# API v3 on dinh cua ScriptHookVDotNet (GTA V Legacy), ghim theo checksum.
$shvdnZip = Join-Path $WorkDir "shvdn-3.6.0.zip"
if (!(Test-Path $shvdnZip)) {
  Invoke-WebRequest -UseBasicParsing "https://github.com/scripthookvdotnet/scripthookvdotnet/releases/download/v3.6.0/ScriptHookVDotNet.zip" -OutFile $shvdnZip
}
$hash = (Get-FileHash $shvdnZip -Algorithm SHA256).Hash.ToLower()
if ($hash -ne "b6b4b61bce0437e295ebab065ca437c95ed938e0169197726150047861478211") { throw "SHVDN 3.6.0 checksum mismatch: $hash" }
Expand-Archive $shvdnZip (Join-Path $WorkDir "shvdn") -Force
$api = Join-Path $WorkDir "shvdn\ScriptHookVDotNet3.dll"

Invoke-Step "Engine tests" { & $Python -m unittest discover -s engine/tests }
Invoke-Step "Wire contract and C# logic tests" { & $Python -m unittest discover -s tests }
Invoke-Step "Bridge tests" { Push-Location bridge; & $Python -m unittest test_engine_context test_news_lock test_dashboard_access test_stt_fallback; Pop-Location }
$logic = Join-Path $WorkDir "logic-bin"
Invoke-Step "Launcher logic tests build" { & $Dotnet build launcher/tests/Logic/GtaVietLife.Launcher.Logic.Tests.csproj -c Release --artifacts-path (Join-Path $WorkDir "logic") -o $logic -v quiet --nologo }
Invoke-Step "Launcher logic tests" { & $Dotnet (Join-Path $logic "GtaVietLife.Launcher.Logic.Tests.dll") }
$control = & $Dotnet (Join-Path $logic "GtaVietLife.Launcher.Logic.Tests.dll") control
if ($LASTEXITCODE -ne 1) { throw "Launcher logic control case did not fail: $control" }

Invoke-Step "Game script assemblies" {
  & (Join-Path $PSScriptRoot "Build-Scripts.ps1") -ShvdnApi3 $api -OutputDir (Join-Path $publish "game") -Dotnet $Dotnet
}
$numeric = $Version.Split("-")[0]
Invoke-Step "Launcher" {
  & $Dotnet publish launcher/GtaVietLife.Launcher/GtaVietLife.Launcher.csproj -c Release -r win-x64 --self-contained true "-p:Version=$numeric" "-p:AssemblyVersion=$numeric.0" "-p:FileVersion=$numeric.0" --artifacts-path (Join-Path $WorkDir "launcher") -o (Join-Path $publish "launcher") -v quiet --nologo
}
Invoke-Step "Updater" {
  & $Dotnet publish launcher/GtaVietLife.Updater/GtaVietLife.Updater.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true --artifacts-path (Join-Path $WorkDir "updater") -o (Join-Path $publish "updater") -v quiet --nologo
}
Invoke-Step "Updater rollback test" { & powershell -NoProfile -ExecutionPolicy Bypass -File launcher/tests/Test-UpdaterRollback.ps1 -UpdaterPath (Join-Path $publish "updater\GtaVietLife.Updater.exe") }
Invoke-Step "Updater health test" { & powershell -NoProfile -ExecutionPolicy Bypass -File launcher/tests/Test-UpdaterHealth.ps1 -UpdaterPath (Join-Path $publish "updater\GtaVietLife.Updater.exe") }
Copy-Item (Join-Path $publish "updater\GtaVietLife.Updater.exe") (Join-Path $publish "launcher")

$pyiWork = Join-Path $WorkDir "pyinstaller"
Invoke-Step "Engine Host executable" {
  & $Python -m PyInstaller --noconfirm --clean --onefile --name GtaVietLifeEngine --workpath $pyiWork --specpath $pyiWork --distpath (Join-Path $publish "runtime\engine") --paths (Join-Path $root "engine") --hidden-import life_simulation --hidden-import society (Join-Path $root "engine\host.py")
}
Invoke-Step "Voice bridge executable" {
  $hidden = foreach ($m in "llm", "memory", "stt", "tts", "overlay", "engine_context", "life", "social") { "--hidden-import"; $m }
  $collect = foreach ($m in "faster_whisper", "ctranslate2", "pygame", "sounddevice", "edge_tts", "numpy") { "--collect-all"; $m }
  & $Python -m PyInstaller --noconfirm --clean --onedir --name GtaVietLifeBridge --paths (Join-Path $root "bridge") @hidden @collect --add-data "$(Join-Path $root 'bridge\config.example.json');." --workpath $pyiWork --specpath $pyiWork --distpath (Join-Path $WorkDir "bridge-dist") (Join-Path $root "bridge\main.py")
}
New-Item -ItemType Directory -Force (Join-Path $publish "runtime\bridge") | Out-Null
Copy-Item (Join-Path $WorkDir "bridge-dist\GtaVietLifeBridge\*") (Join-Path $publish "runtime\bridge") -Recurse -Force
Copy-Item bridge\config.example.json (Join-Path $publish "runtime\bridge")

# manifest.json: moi file trong bo cai kem SHA-256, Launcher kiem luc khoi dong.
$layout = Join-Path $WorkDir "layout"
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Force $layout | Out-Null
Copy-Item (Join-Path $publish "launcher\*") $layout -Recurse -Force
Copy-Item (Join-Path $publish "runtime") (Join-Path $layout "runtime") -Recurse -Force
Copy-Item (Join-Path $publish "game") (Join-Path $layout "game") -Recurse -Force
$files = [ordered]@{}
Get-ChildItem $layout -Recurse -File | Sort-Object FullName | ForEach-Object {
  $relative = $_.FullName.Substring($layout.Length + 1).Replace("\", "/")
  $files[$relative] = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()
}
$mloPacks = @(Get-Content (Join-Path $PSScriptRoot "mlo-packs.json") -Raw | ConvertFrom-Json)
$manifest = [ordered]@{ version = $Version; target = "gta5-legacy"; files = $files; mlo_packs = $mloPacks }
$json = $manifest | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $layout "manifest.json"), $json, (New-Object Text.UTF8Encoding($false)))
Copy-Item (Join-Path $layout "manifest.json") (Join-Path $publish "manifest.json")

Invoke-Step "Packaged services smoke test" { & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "Test-PackagedServices.ps1") -Layout $layout }

$zip = Join-Path $artifacts "GtaVietLife-Windows-x64.zip"
Compress-Archive -Path (Join-Path $layout "*") -DestinationPath $zip -Force
Invoke-Step "Installer" { & $InnoCompiler /Q "/DMyAppVersion=$Version" "/DPublishDir=$publish" packaging/GtaVietLife.iss }
$sums = Get-ChildItem $artifacts -File | ForEach-Object { "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower())  $($_.Name)" }
$sums | Out-File (Join-Path $artifacts "SHA256SUMS.txt") -Encoding ascii
Get-ChildItem $artifacts | Select-Object Name, Length | Format-Table -AutoSize | Out-String | Write-Host
