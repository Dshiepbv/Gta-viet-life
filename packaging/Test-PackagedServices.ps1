# Chay that cac file .exe da dong goi: Engine Host (hai lan, cung du lieu) va cau noi AI.
param(
  [Parameter(Mandatory = $true)][string]$Layout,
  [int]$EnginePort = 5590
)
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"
$work = Join-Path ([IO.Path]::GetTempPath()) ("gvl-smoke-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $work | Out-Null
$token = "s" * 48
[IO.File]::WriteAllText((Join-Path $work "token"), $token)
$headers = @{ "X-GVL-Token" = $token }
$base = "http://127.0.0.1:$EnginePort"

function Wait-Health([string]$Url, $Process, [int]$Seconds, [string]$What) {
  $deadline = (Get-Date).AddSeconds($Seconds)
  while ((Get-Date) -lt $deadline) {
    if ($Process.HasExited) { throw "$What exited before becoming healthy" }
    try { return Invoke-RestMethod $Url -TimeoutSec 2 } catch { Start-Sleep -Milliseconds 500 }
  }
  throw "$What did not answer $Url"
}
function Stop-Tree($Process) {
  if ($Process -and !$Process.HasExited) { cmd /c "taskkill /PID $($Process.Id) /T /F >nul 2>&1" }
}
function Post([string]$Path, [string]$Body) {
  Invoke-RestMethod "$base$Path" -Method Post -Headers $headers -Body $Body -ContentType "application/json"
}

$engine = $null; $bridge = $null
try {
  foreach ($round in 1, 2) {
    $engine = Start-Process (Join-Path $Layout "runtime\engine\GtaVietLifeEngine.exe") -PassThru -WindowStyle Hidden `
      -RedirectStandardError (Join-Path $work "engine-$round.err") -RedirectStandardOutput (Join-Path $work "engine-$round.out") `
      -ArgumentList "--port", "$EnginePort", "--token-file", "`"$work\token`"", "--data-dir", "`"$work\engine`""
    try { Wait-Health "$base/health" $engine 60 "Engine Host" | Out-Null }
    catch { Get-Content (Join-Path $work "engine-$round.err") -Tail 15 | Write-Host; throw }
    $npc = '{"request_id":"r' + $round + '","npc":{"npc_id":"res_1","display_name":"Lan","home_location_id":"res_1.home","occupation":"resident","life_goal":"social","traits":["community_resident"],"schedule":[]}}'
    if (!(Post "/engine/v1/simulation/npcs" $npc).registered) { throw "resident not registered in round $round" }
    $events = '{"request_id":"e' + $round + '","source_id":"gta5.shvdn","events":[{"sequence":' + $round + ',"event_type":"wallet.observed","data":"balance=' + (700 + $round) + ';char=SP0"}]}'
    if ((Post "/engine/v1/world/events" $events).accepted -ne 1) { throw "wallet event rejected in round $round" }
    # The Vietnam Data Pack must be bundled inside the engine executable.
    $quote = Post "/engine/v1/world/prices/quote" ('{"request_id":"q' + $round + '","item_id":"food_001","zone":"STRAW"}')
    if ($quote.currency -ne "VND" -or $quote.price -lt 35000) { throw "data pack missing from the packaged engine" }
    Post "/engine/v1/shutdown" '{"request_id":"stop"}' | Out-Null
    if (!$engine.WaitForExit(20000)) { throw "Engine Host did not stop" }
    Write-Host "engine round ${round}: ok"
  }

  if (Get-NetTCPConnection -LocalPort 5577 -State Listen -ErrorAction SilentlyContinue) { throw "port 5577 is busy; cannot test the bridge" }
  $env:GVL_DATA_DIR = Join-Path $work "bridge"
  New-Item -ItemType Directory -Force $env:GVL_DATA_DIR | Out-Null
  Copy-Item (Join-Path $Layout "runtime\bridge\config.example.json") (Join-Path $env:GVL_DATA_DIR "config.json")
  $bridge = Start-Process (Join-Path $Layout "runtime\bridge\GtaVietLifeBridge.exe") -PassThru -WindowStyle Hidden `
    -RedirectStandardError (Join-Path $work "bridge.err") -RedirectStandardOutput (Join-Path $work "bridge.out")
  try { $health = Wait-Health "http://127.0.0.1:5577/health" $bridge 180 "Voice bridge" }
  catch { Get-Content (Join-Path $work "bridge.err"), (Join-Path $work "bridge.out") -Tail 25 | Write-Host; throw }
  if ($health.service -ne "gta-viet-life-ai-bridge") { throw "unexpected bridge identity" }
  Write-Host "bridge: ok"
}
finally {
  Remove-Item Env:GVL_DATA_DIR -ErrorAction SilentlyContinue
  Stop-Tree $engine; Stop-Tree $bridge
  Start-Sleep -Seconds 1
  Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
