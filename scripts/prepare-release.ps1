param(
  [Parameter(Mandatory = $true)][string]$Version,  # e.g., 0.2.0 or 0.2.0-beta.1
  [string]$Csproj = "StartUp.csproj",
  [string]$Configuration = "Release",
  [string]$Rid = "win-x64",
  [string]$Tfm = "net8.0-windows10.0.19041.0"
)

Write-Host "Preparing release for version $Version"

$IsPre   = $Version -like "*-*"
$BaseVer = ($Version -split "-", 2)[0]    # numeric part only
[version]$Base = $BaseVer
$AsmVer  = "$($Base.Major).$($Base.Minor).$($Base.Build).0"

if (-not (Test-Path $Csproj)) { throw "Cannot find $Csproj" }

[xml]$xml = Get-Content $Csproj
$ns = $xml.Project.NamespaceURI
$pg = $xml.Project.PropertyGroup
if (-not $pg) { $pg = $xml.CreateElement("PropertyGroup", $ns); $xml.Project.AppendChild($pg) | Out-Null }

function Set-Node([xml]$x, $parent, $name, $value) {
  $node = $parent.$name
  if (-not $node) { $node = $x.CreateElement($name, $ns); $parent.AppendChild($node) | Out-Null }
  $node.InnerText = $value
}

if (-not $IsPre) {
  # Stable: bump all fields to BaseVer (with downgrade guard)
  $current = $pg.Version.InnerText
  if ($current) {
    [version]$curVer = $current
    if ($curVer -gt $Base) { throw "Refusing to downgrade Version from $curVer to $Base" }
  }
  Set-Node $xml $pg "Version" $BaseVer
  Set-Node $xml $pg "AssemblyVersion" $AsmVer
  Set-Node $xml $pg "FileVersion" $AsmVer
  Set-Node $xml $pg "InformationalVersion" $BaseVer
  Write-Host "Stable: Version=$BaseVer Assembly/File=$AsmVer Info=$BaseVer"
}
else {
  # Pre-release: keep <Version> as current stable; set Info to full; Assembly/File to base numeric
  Set-Node $xml $pg "AssemblyVersion" $AsmVer
  Set-Node $xml $pg "FileVersion" $AsmVer
  Set-Node $xml $pg "InformationalVersion" $Version
  Write-Host "Pre-release: Info=$Version Assembly/File=$AsmVer (Version unchanged)"
}

$xml.Save($Csproj)

# Build & zip so @semantic-release/github can upload it
Write-Host "Building $Csproj ($Configuration, $Rid, $Tfm)"
$publishRoot = Join-Path (Split-Path -Parent $Csproj) "bin/$Configuration/$Tfm/$Rid/publish"
dotnet publish $Csproj `
  -c $Configuration `
  -r $Rid `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -p:IncludeNativeLibrariesForSelfExtract=true

if (-not (Test-Path $publishRoot)) { throw "Publish output not found at $publishRoot" }

if (Test-Path "BootWorkspace.zip") { Remove-Item -Force "BootWorkspace.zip" }
Compress-Archive -Path "$publishRoot/*" -DestinationPath "BootWorkspace.zip"
