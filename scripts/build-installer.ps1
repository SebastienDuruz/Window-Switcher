param(
    [string] $Configuration = "Release",
    [string] $Runtime = "win-x64",
    [string] $Version = "0.6.2",
    [switch] $SelfContained,
    [string] $PublishDir = (Join-Path $PSScriptRoot "..\\artifacts\\publish\\$Runtime"),
    [string] $OutDir = (Join-Path $PSScriptRoot "..\\artifacts\\installer")
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $repoRoot "WindowSwitcher\\WindowSwitcher.csproj"
$nsi = Join-Path $repoRoot "installer\\WindowSwitcher.nsi"

New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$env:AVALONIA_TELEMETRY_OPTOUT = "1"

$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "-o", $PublishDir,
    "-p:Version=$Version",
    "-p:InformationalVersion=$Version"
)

$semver = [regex]::Match($Version, "^(\\d+)\\.(\\d+)\\.(\\d+)$")
if ($semver.Success) {
    $fourPart = "$($semver.Groups[1].Value).$($semver.Groups[2].Value).$($semver.Groups[3].Value).0"
    $publishArgs += "-p:AssemblyVersion=$fourPart"
    $publishArgs += "-p:FileVersion=$fourPart"
}

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

$setupName = "WindowSwitcher-Setup-$Runtime-$Version.exe"
$outFile = Join-Path $OutDir $setupName

$makensisCmd = Get-Command "makensis.exe" -ErrorAction SilentlyContinue
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
    throw "NSIS not found. Install NSIS (makensis.exe), then re-run this script."
}

Write-Host "Building installer..." -ForegroundColor Cyan
& $makensis "/DAPP_VERSION=$Version" "/DPUBLISH_DIR=$PublishDir" "/DOUT_FILE=$outFile" $nsi | Write-Host

Write-Host "Installer created: $outFile" -ForegroundColor Green
