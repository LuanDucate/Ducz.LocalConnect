param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "Ducz.LocalConnect.App\Ducz.LocalConnect.App.csproj"
$installerScript = Join-Path $repoRoot "installer\DuczLocalConnect.iss"
$publishDir = Join-Path $repoRoot "Ducz.LocalConnect.App\bin\$Configuration\net10.0-windows\$Runtime\publish"

Write-Host "Publishing Ducz LocalConnect..."
dotnet publish $projectPath -c $Configuration -r $Runtime --self-contained true -p:Version=$Version

if (-not (Test-Path $publishDir)) {
    throw "Publish output not found at $publishDir"
}

$isccCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)

$isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $isccPath) {
    throw "Inno Setup was not found. Install it from https://jrsoftware.org/isinfo.php and run the script again."
}

Write-Host "Building installer with Inno Setup..."
& $isccPath "/DMyAppVersion=$Version" $installerScript

Write-Host "Installer created under the dist folder."
