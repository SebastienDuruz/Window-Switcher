param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Version = "",
    [switch] $SelfContained,
    [string] $PublishDir = (Join-Path $PSScriptRoot "..\\artifacts\\publish\\$Runtime"),
    [string] $OutDir = (Join-Path $PSScriptRoot "..\\artifacts\\installer")
)

$ErrorActionPreference = "Stop"

function Get-WindowSwitcherVersion {
    param([string] $PropsPath)

    if (-not (Test-Path $PropsPath)) {
        throw "Version file not found: $PropsPath"
    }

    [xml] $props = Get-Content -Path $PropsPath -Raw
    $versionNode = $props.SelectSingleNode("/Project/PropertyGroup/WindowSwitcherVersion")

    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) {
        throw "WindowSwitcherVersion not found in: $PropsPath"
    }

    return $versionNode.InnerText.Trim()
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "src\\WindowSwitcher\\WindowSwitcher.csproj"
$nsi = Join-Path $repoRoot "installer\\WindowSwitcher.nsi"
$versionProps = Join-Path $repoRoot "Directory.Build.props"

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-WindowSwitcherVersion -PropsPath $versionProps
}

New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$env:AVALONIA_TELEMETRY_OPTOUT = "1"

$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $PublishDir,
    "-p:UsedAvaloniaProducts=",
    "-p:Version=$Version",
    "-p:PackageVersion=$Version",
    "-p:InformationalVersion=$Version"
)

if ($SelfContained) {
    $publishArgs += "--self-contained"
    $publishArgs += "true"
}
else {
    $publishArgs += "--self-contained"
    $publishArgs += "false"
}

Write-Host "Publishing..." -ForegroundColor Cyan
dotnet @publishArgs

$setupName = "WindowSwitcher-setup-$Version-$Runtime.exe"
$outFile = Join-Path $OutDir $setupName

$makensisCmd = Get-Command "makensis.exe" -ErrorAction SilentlyContinue
if (-not $makensisCmd) {
    $makensisCmd = Get-Command "makensis" -ErrorAction SilentlyContinue
}
$makensis = $null
if ($makensisCmd) {
    $makensis = $makensisCmd.Source
}
if (-not $makensis) {
    $candidate = "${env:ProgramFiles(x86)}\\NSIS\\makensis.exe"
    if (Test-Path $candidate) {
        $makensis = $candidate
    }
}

if (-not $makensis) {
    throw "NSIS not found. Install NSIS (makensis), then re-run this script."
}

Write-Host "Building installer..." -ForegroundColor Cyan
if ($IsWindows -or $makensis.EndsWith(".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
    & $makensis "/DAPP_VERSION=$Version" "/DPUBLISH_DIR=$PublishDir" "/DOUT_FILE=$outFile" $nsi | Write-Host
}
else {
    & $makensis "-DAPP_VERSION=$Version" "-DPUBLISH_DIR=$PublishDir" "-DOUT_FILE=$outFile" $nsi | Write-Host
}

Write-Host "Installer created: $outFile" -ForegroundColor Green
