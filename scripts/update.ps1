# AgentManager standalone updater.
# Launched by the app (About > Update) as a separate process so the app exe is unlocked.
# Flow: wait for the app to exit -> fast-forward pull the current branch -> rebuild (if changed) -> relaunch.
# Build outputs (dist/, bin/) are gitignored, so the pull never conflicts with the running binary.
param(
  [int]$WaitPid = 0,
  [string]$Relaunch = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot   # scripts/ lives under the repo root

Write-Host "=== AgentManager Updater ===" -ForegroundColor Cyan
Write-Host "Repo: $root`n"

# 1) Wait for the running app to exit so its exe is unlocked.
if ($WaitPid -gt 0) {
  Write-Host "Waiting for AgentManager (PID $WaitPid) to exit..."
  try { Wait-Process -Id $WaitPid -Timeout 60 -ErrorAction SilentlyContinue } catch {}
  Start-Sleep -Milliseconds 400
}

try {
  $branch = (git -C $root rev-parse --abbrev-ref HEAD).Trim()
  $before = (git -C $root rev-parse HEAD).Trim()

  Write-Host "`n[1/3] Pulling latest from origin/$branch ..." -ForegroundColor Cyan
  git -C $root fetch origin
  git -C $root pull --ff-only origin $branch
  if ($LASTEXITCODE -ne 0) { throw "git pull failed (local changes or diverged branch). Resolve manually, then retry." }

  $after = (git -C $root rev-parse HEAD).Trim()
  $distExe = Join-Path $root "dist\AgentManager.exe"

  if ($before -eq $after -and (Test-Path $distExe)) {
    Write-Host "`n[2/3] Already up to date - skipping build." -ForegroundColor Green
  } else {
    Write-Host "`n[2/3] Building..." -ForegroundColor Cyan
    & "$root\scripts\publish.ps1"
    if ($LASTEXITCODE -ne 0) { throw "build failed." }
  }

  Write-Host "`n[3/3] Restarting AgentManager..." -ForegroundColor Cyan
  $target = if ($Relaunch -and (Test-Path $Relaunch)) { $Relaunch } else { $distExe }
  Start-Process $target
  Write-Host "`nDone." -ForegroundColor Green
  Start-Sleep -Seconds 2
}
catch {
  Write-Host "`nUpdate failed: $($_.Exception.Message)" -ForegroundColor Red
  Write-Host "Press Enter to close..."
  Read-Host | Out-Null
}
