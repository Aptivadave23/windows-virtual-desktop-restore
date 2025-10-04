#Requires -Version 7.0
<#
.SYNOPSIS
Publishes BootWorkspace and (optionally) installs it for the current user.

.DESCRIPTION
- Restores and publishes the project (single-file, self-contained .NET 8 by default)
- Ensures a workspace.json is present in the publish folder (copies one from repo if found, else creates a minimal default)
- Optionally runs the app with --install so it sets up the Startup shortcut

.PARAMETER Install
If provided, runs the built EXE with --install after publishing.

.PARAMETER Configuration
Build configuration. Default: Release

.PARAMETER Runtime
Target runtime identifier (RID). Default: win-x64

.PARAMETER TFM
Target framework moniker for output path resolution. Default: net8.0-windows10.0.19041.0

.PARAMETER ProjectPath
Optional explicit path to the .csproj. If omitted, the script picks the first .csproj in the script directory.

.PARAMETER WorkspaceJson
Optional path to a workspace.json to copy into the publish directory.

.EXAMPLE
pwsh -File .\release-publish.ps1 -Install

.EXAMPLE
powershell.exe -ExecutionPolicy Bypass -File .\release-publish.ps1 -Configuration Debug -WorkspaceJson .\workspace.json

.NOTES
SPDX-License-Identifier: Unlicense
#>

[CmdletBinding()]
param(
  [switch]$Install,
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [string]$TFM = "net8.0-windows10.0.19041.0",
  [string]$ProjectPath,
  [string]$WorkspaceJson
)

$ErrorActionPreference = "Stop"

function Write-Section([string]$Text) {
  Write-Host ("`n=== {0} ===`n" -f $Text) -ForegroundColor Cyan
}
function Write-Ok([string]$Text) {
  Write-Host $Text -ForegroundColor Green
}
function Write-Warn([string]$Text) {
  Write-Warning $Text
}

# Resolve script root
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path

# Resolve project
if (-not $ProjectPath) {
  $csprojs = Get-ChildItem -Path $ScriptRoot -Filter *.csproj | Sort-Object Name
  if ($csprojs.Count -eq 0) { throw "No .csproj found in $ScriptRoot" }
  elseif ($csprojs.Count -gt 1) {
    Write-Warn "Multiple .csproj files found. Picking the first: $($csprojs[0].FullName). Use -ProjectPath to override."
  }
  $ProjectPath = $csprojs[0].FullName
}
elseif (-not (Test-Path $ProjectPath)) {
  throw "ProjectPath not found: $ProjectPath"
}

Write-Section "Project"
Write-Host "Project: $ProjectPath"
Write-Host "TFM:     $TFM"
Write-Host "RID:     $Runtime"
Write-Host "Config:  $Configuration"

# Ensure dotnet is available
try {
  $dotnetVersion = (dotnet --version).Trim()
  Write-Host "dotnet:  $dotnetVersion"
}
catch {
  throw "The .NET SDK (dotnet) was not found on PATH."
}

# Restore
Write-Section "Restore"
dotnet restore $ProjectPath

# Publish (single-file, self-contained)
Write-Section "Publish"
$pubArgs = @(
  "publish", $ProjectPath,
  "-c", $Configuration,
  "-r", $Runtime,
  "--self-contained", "true",
  "-p:PublishSingleFile=true",
  "-p:PublishTrimmed=false"
)
dotnet @pubArgs

# Compute publish directory
$PublishDir = Join-Path $ScriptRoot "bin\$Configuration\$TFM\$Runtime\publish"
if (-not (Test-Path $PublishDir)) {
  throw "Publish directory not found: $PublishDir"
}
Write-Ok "Publish complete: $PublishDir"

# Determine exe path
$ProjectName = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)
$ExePath = Join-Path $PublishDir ($ProjectName + ".exe")
if (-not (Test-Path $ExePath)) {
  $exeFiles = Get-ChildItem $PublishDir -Filter *.exe | Select-Object -First 1
  if (-not $exeFiles) { throw "No .exe found in $PublishDir" }
  $ExePath = $exeFiles.FullName
}
Write-Host "EXE:     $ExePath"

# Ensure workspace.json in publish
$RepoCfg = $null
if ($WorkspaceJson) {
  if (-not (Test-Path $WorkspaceJson)) { throw "WorkspaceJson not found: $WorkspaceJson" }
  $RepoCfg = (Resolve-Path $WorkspaceJson).Path
} else {
  $maybe = Join-Path $ScriptRoot "workspace.json"
  if (Test-Path $maybe) { $RepoCfg = $maybe }
}

$PubCfg = Join-Path $PublishDir "workspace.json"

Write-Section "Config"
if ($RepoCfg) {
  Copy-Item $RepoCfg $PubCfg -Force
  Write-Ok "Copied workspace.json to publish folder."
} elseif (-not (Test-Path $PubCfg)) {
  $defaultJson = @'
{
  "desktops": [
    { "index": 0, "name": "Thing 1" },
    { "index": 1, "name": "Thing 2" }
  ],
  "apps": [
    { "name": "Outlook", "path": "C:\\\\Program Files\\\\Microsoft Office\\\\root\\\\Office16\\\\OUTLOOK.EXE", "desktop": "Thing 1" }
  ],
  "launchDelayMs": 1500
}
'@
  $defaultJson | Set-Content -Path $PubCfg -Encoding utf8
  Write-Ok "Created default workspace.json in publish folder."
} else {
  Write-Ok "workspace.json already exists in publish folder."
}

# Optional install
if ($Install) {
  Write-Section "Install"
  & $ExePath --install $PubCfg
  Write-Ok "Installed. Verify: 'shell:startup' contains 'Boot Workspace.lnk'."
  Write-Host "Log: %LOCALAPPDATA%\BootWorkspace\bootworkspace.log"
} else {
  Write-Section "Next Steps"
  Write-Host "Run the installer when ready:" -ForegroundColor Yellow
  Write-Host "  `"$ExePath`" --install `"$PubCfg`"" -ForegroundColor Yellow
}

Write-Section "Done"
Write-Ok "All good."
