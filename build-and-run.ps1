# Build and run InputMonitorMapper
# Requires: .NET 8 SDK (https://dotnet.microsoft.com/download/dotnet/8.0)

$ErrorActionPreference = "Stop"
$projectDir = $PSScriptRoot

# Try to find dotnet (PATH or common install locations)
$dotnet = $null
if (Get-Command dotnet -ErrorAction SilentlyContinue) {
    $dotnet = "dotnet"
} elseif (Test-Path "C:\Program Files\dotnet\dotnet.exe") {
    $dotnet = "C:\Program Files\dotnet\dotnet.exe"
} elseif (Test-Path "$env:LOCALAPPDATA\Programs\dotnet\dotnet.exe") {
    $dotnet = "$env:LOCALAPPDATA\Programs\dotnet\dotnet.exe"
}

if (-not $dotnet) {
    Write-Host "ERROR: .NET SDK not found." -ForegroundColor Red
    Write-Host ""
    Write-Host "Install the .NET 8 SDK, then run this script again:"
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0"
    Write-Host ""
    Write-Host "Choose: Windows x64 - Run the installer, then close and reopen PowerShell."
    exit 1
}

Set-Location $projectDir
Write-Host "Building..." -ForegroundColor Cyan
& $dotnet build -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Running..." -ForegroundColor Cyan
& $dotnet run -c Release --no-build
exit $LASTEXITCODE
