$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
dotnet publish "$root\src\AgentManager\AgentManager.csproj" -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true -o "$root\dist"
Write-Host "Published to $root\dist\AgentManager.exe"
