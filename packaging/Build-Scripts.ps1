# Bien dich moi script GTA thanh mot DLL cho ScriptHookVDotNet v3, kem ini va icon.
param(
  [Parameter(Mandatory = $true)][string]$ShvdnApi3,
  [Parameter(Mandatory = $true)][string]$OutputDir,
  [string]$Dotnet = "dotnet"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$work = Join-Path ([IO.Path]::GetTempPath()) ("gvl-scripts-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $OutputDir, $work | Out-Null
try {
  $projects = Get-ChildItem (Join-Path $root "game\compile") -Filter "*.Compile.csproj" | Sort-Object Name
  foreach ($project in $projects) {
    $name = ([xml](Get-Content $project.FullName)).Project.PropertyGroup.AssemblyName
    $out = Join-Path $work $name
    & $Dotnet build $project.FullName -c Release "-p:ShvdnApi3=$ShvdnApi3" --artifacts-path (Join-Path $work "a-$name") -o $out -v quiet --nologo
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $($project.Name)" }
    Copy-Item (Join-Path $out "$name.dll") $OutputDir -Force
  }
  Copy-Item (Join-Path $root "game\*.ini") $OutputDir -Force
  Copy-Item (Join-Path $root "game\GtaVietLifeIcons") $OutputDir -Recurse -Force
  # Hang hoa va gia trong cua hang doc tu Data Pack (chi doc).
  $data = Join-Path $OutputDir "GtaVietLifeData\vi-VN"
  New-Item -ItemType Directory -Force $data | Out-Null
  Copy-Item (Join-Path $root "data\packs\vi-VN\food_drink_menu.json"), (Join-Path $root "data\packs\vi-VN\goods_prices.json") $data -Force
  Write-Host ("Built " + $projects.Count + " script assemblies into " + $OutputDir)
} finally {
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
