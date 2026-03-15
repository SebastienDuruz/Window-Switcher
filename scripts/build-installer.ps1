param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Version = "",
    [switch] $SelfContained,
    [string] $PublishDir = [System.IO.Path]::Combine($PSScriptRoot, "artifacts", "publish", $Runtime),
    [string] $OutDir = [System.IO.Path]::Combine($PSScriptRoot, "artifacts", "installer")
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
$project = [System.IO.Path]::Combine($repoRoot, "src", "WindowSwitcher", "WindowSwitcher.csproj")
$nsi = [System.IO.Path]::Combine($repoRoot, "scripts", "assets", "installer", "WindowSwitcher.nsi")
$versionProps = [System.IO.Path]::Combine($repoRoot, "Directory.Build.props")

if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = Get-WindowSwitcherVersion -PropsPath $versionProps
}

$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

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
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedEntries = @(Get-ChildItem -Path $PublishDir -Force -ErrorAction Stop)
if ($publishedEntries.Count -eq 0) {
    throw "dotnet publish produced no files in: $PublishDir"
}

$setupName = "WindowSwitcher-setup-$Version-$Runtime.exe"
$outFile = Join-Path $OutDir $setupName
$publishGlob = Join-Path $PublishDir "*"

$makensisCmd = Get-Command "makensis.exe" -ErrorAction SilentlyContinue
if (-not $makensisCmd) {
    $makensisCmd = Get-Command "makensis" -ErrorAction SilentlyContinue
}
$makensis = $null
if ($makensisCmd) {
    $makensis = $makensisCmd.Source
}
if (-not $makensis) {
    $candidate = [System.IO.Path]::Combine(${env:ProgramFiles(x86)}, "NSIS", "makensis.exe")
    if (Test-Path $candidate) {
        $makensis = $candidate
    }
}

if (-not $makensis) {
    throw "NSIS not found. Install NSIS (makensis), then re-run this script."
}

Write-Host "Building installer..." -ForegroundColor Cyan
if ($IsWindows -or $makensis.EndsWith(".exe", [System.StringComparison]::OrdinalIgnoreCase)) {
    & $makensis "/DAPP_VERSION=$Version" "/DPUBLISH_DIR=$PublishDir" "/DPUBLISH_GLOB=$publishGlob" "/DOUT_FILE=$outFile" $nsi | Write-Host
}
else {
    & $makensis "-DAPP_VERSION=$Version" "-DPUBLISH_DIR=$PublishDir" "-DPUBLISH_GLOB=$publishGlob" "-DOUT_FILE=$outFile" $nsi | Write-Host
}

if ($LASTEXITCODE -ne 0) {
    throw "makensis failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -Path $outFile -PathType Leaf)) {
    throw "Installer was not created at: $outFile"
}

Write-Host "Installer created: $outFile" -ForegroundColor Green
