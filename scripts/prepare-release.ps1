param(
  [Parameter(Mandatory = $true)][string]$Version,           # e.g., 0.2.0 or 0.2.0-beta.1
  [string]$Csproj = "StartUp.csproj",
  [string]$Configuration = "Release",
  [string]$Rid = "win-x64",
  [string]$Tfm = "net8.0-windows10.0.19041.0"
)

$ErrorActionPreference = 'Stop'
Write-Host "Preparing release for version $Version"

# ---- Resolve absolute paths so Split-Path works ----
$csprojFull = (Resolve-Path -LiteralPath $Csproj).Path
$projDir    = Split-Path -Parent $csprojFull

$IsPre   = $Version -like "*-*"
$BaseVer = ($Version -split "-", 2)[0]   # numeric part only
[version]$Base = $BaseVer
$AsmVer  = "$($Base.Major).$($Base.Minor).$($Base.Build).0"

# ---- Load XML and pick a single PropertyGroup safely ----
[xml]$xml = Get-Content -LiteralPath $csprojFull
$ns  = $xml.DocumentElement.NamespaceURI

# Ensure there is at least one <PropertyGroup>
$pgNodes = @($xml.Project.PropertyGroup)
if (-not $pgNodes -or $pgNodes.Count -eq 0) {
  $newPg = $xml.CreateElement("PropertyGroup", $ns)
  [void]$xml.Project.AppendChild($newPg)
  $pg = $newPg
} else {
  $pg = $pgNodes[0]   # use the first PG; adjust if you want a specific one
}

# Helper: get or create a child element by local-name() (namespace-agnostic)
function Set-ElementValue {
  param(
    [xml]$doc,
    [System.Xml.XmlElement]$parent,
    [string]$name,
    [string]$value,
    [string]$nsUri
  )
  $node = $parent.SelectSingleNode("./*[local-name()='$name']")
  if (-not $node) {
    $node = $doc.CreateElement($name, $nsUri)
    [void]$parent.AppendChild($node)
  }
  $node.InnerText = $value
}

# Helper: read a child element value by local-name()
function Get-ElementValue {
  param(
    [System.Xml.XmlElement]$parent,
    [string]$name
  )
  $n = $parent.SelectSingleNode("./*[local-name()='$name']")
  if ($n) { return $n.InnerText } else { return $null }
}

if (-not $IsPre) {
  # ----- STABLE: bump all fields to BaseVer (with downgrade guard) -----
  $currentText = Get-ElementValue -parent $pg -name "Version"
  if ($currentText) {
    [version]$curVer = $currentText
    if ($curVer -gt $Base) { throw "Refusing to downgrade Version from $curVer to $Base" }
  }
  Set-ElementValue -doc $xml -parent $pg -name "Version"              -value $BaseVer -nsUri $ns
  Set-ElementValue -doc $xml -parent $pg -name "AssemblyVersion"      -value $AsmVer  -nsUri $ns
  Set-ElementValue -doc $xml -parent $pg -name "FileVersion"          -value $AsmVer  -nsUri $ns
  Set-ElementValue -doc $xml -parent $pg -name "InformationalVersion" -value $BaseVer -nsUri $ns
  Write-Host "Stable: Version=$BaseVer Assembly/File=$AsmVer Info=$BaseVer"
}
else {
  # ----- PRE-RELEASE: keep <Version>, set Info to full, Assembly/File to base numeric -----
  Set-ElementValue -doc $xml -parent $pg -name "AssemblyVersion"      -value $AsmVer  -nsUri $ns
  Set-ElementValue -doc $xml -parent $pg -name "FileVersion"          -value $AsmVer  -nsUri $ns
  Set-ElementValue -doc $xml -parent $pg -name "InformationalVersion" -value $Version -nsUri $ns
  Write-Host "Pre-release: Info=$Version Assembly/File=$AsmVer (Version unchanged)"
}

$xml.Save($csprojFull)

# ---- Build & zip publish output ----
Write-Host "Building $csprojFull ($Configuration, $Rid, $Tfm)"
$publishRoot = Join-Path $projDir "bin/$Configuration/$Tfm/$Rid/publish"

dotnet publish $csprojFull `
  -c $Configuration `
  -r $Rid `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:PublishTrimmed=false `
  -p:IncludeNativeLibrariesForSelfExtract=true

if (-not (Test-Path -LiteralPath $publishRoot)) {
  throw "Publish output not found at $publishRoot"
}

$zipPath = Join-Path $projDir "BootWorkspace.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $publishRoot '*') -DestinationPath $zipPath
Write-Host "Zipped to $zipPath"
