# AgentManager release packer (Velopack).
# Self-contained publish (bundles the .NET runtime — no .NET needed on the target) -> vpk pack ->
# Releases\AgentManager-win-Setup.exe + the update feed (RELEASES + full/delta .nupkg).
# Usage:  scripts\release.ps1            # version from the csproj (pack only)
#         scripts\release.ps1 1.18.0     # explicit version
#         scripts\release.ps1 1.18.0 -Sign "/a /f cert.pfx /p pass"   # optional Authenticode signing
#         scripts\release.ps1 -Upload    # pack, then publish the feed to the GitHub Release (needs $env:GITHUB_TOKEN)
param(
  [string]$Version = "",
  [string]$Sign = "",    # vpk --signParams value (signtool args); empty = unsigned
  [switch]$Upload        # after packing, upload+publish Releases\ to the GitHub Release for tag v$Version
)

$repoUrl = "https://github.com/Bo-sung/AgentManager"

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root "src\AgentManager\AgentManager.csproj"

if (-not $Version) {
  $Version = (Select-String -Path $csproj -Pattern '<Version>(.*?)</Version>').Matches[0].Groups[1].Value
}
Write-Host "=== Packing AgentManager v$Version ===" -ForegroundColor Cyan

$pub = Join-Path $root "publish"
$rel = Join-Path $root "Releases"
Remove-Item $pub -Recurse -Force -ErrorAction SilentlyContinue

# 1) Self-contained folder publish (NOT single-file — Velopack manages the app folder + delta updates).
Write-Host "`n[1/2] dotnet publish (self-contained win-x64)..." -ForegroundColor Cyan
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -o $pub
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

# 2) vpk pack -> installer + update feed.
Write-Host "`n[2/2] vpk pack..." -ForegroundColor Cyan
$signArgs = @()
if ($Sign) { $signArgs = @("--signParams", $Sign) }
vpk pack `
  --packId AgentManager `
  --packTitle "AgentManager" `
  --packVersion $Version `
  --packDir $pub `
  --mainExe AgentManager.exe `
  --icon "$root\src\AgentManager\Resources\AppIcon.ico" `
  --outputDir $rel `
  @signArgs
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Write-Host "`nDone -> $rel" -ForegroundColor Green
Get-ChildItem $rel | Select-Object Name, @{n='MB';e={[math]::Round($_.Length/1MB,1)}} | Format-Table

# 3) Optional: publish the feed to the GitHub Release so installed builds actually see the update.
#    Token comes ONLY from the environment (never a literal) — needs `repo` (classic) or contents:write (fine-grained).
if ($Upload) {
  if (-not $env:GITHUB_TOKEN) { throw "Upload requested but `$env:GITHUB_TOKEN is not set. Set it (User scope) and restart, then re-run." }
  Write-Host "`n[3/3] vpk upload github (tag v$Version, publish)..." -ForegroundColor Cyan
  vpk upload github `
    --outputDir $rel `
    --repoUrl $repoUrl `
    --tag "v$Version" `
    --targetCommitish master `
    --publish true `
    --merge true `
    --token $env:GITHUB_TOKEN
  if ($LASTEXITCODE -ne 0) { throw "vpk upload github failed" }
  Write-Host "Published -> $repoUrl/releases/tag/v$Version" -ForegroundColor Green
} else {
  Write-Host "Ship: re-run with -Upload (or upload Releases\ to the GitHub Release manually: Setup.exe + RELEASES + *.nupkg)." -ForegroundColor Yellow
}
