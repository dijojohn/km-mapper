# Install .NET 8 SDK from PowerShell only (no browser).
# Run: .\install-dotnet-sdk.ps1
# Requires: Run as Administrator if using winget, or allow script to run the downloaded installer.

$ErrorActionPreference = "Stop"

function Test-DotNetInstalled {
    try {
        $null = Get-Command dotnet -ErrorAction Stop
        $ver = (dotnet --version 2>$null)
        return $ver -match "^8\."
    } catch {
        return $false
    }
}

if (Test-DotNetInstalled) {
    Write-Host ".NET 8 SDK is already installed (dotnet --version: $(dotnet --version))." -ForegroundColor Green
    exit 0
}

Write-Host "Installing .NET 8 SDK..." -ForegroundColor Cyan

# 1) Try winget (built-in on Windows 10 21H2+ and Windows 11)
$winget = Get-Command winget -ErrorAction SilentlyContinue
if ($winget) {
    Write-Host "Using winget..." -ForegroundColor Cyan
    try {
        winget install --id Microsoft.DotNet.SDK.8 --exact --accept-package-agreements --accept-source-agreements --silent
        if ($LASTEXITCODE -eq 0) {
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
            if (Test-DotNetInstalled) {
                Write-Host "Installation succeeded. Run 'dotnet --version' in a new PowerShell window." -ForegroundColor Green
                exit 0
            }
        }
    } catch {
        Write-Host "winget install failed: $_" -ForegroundColor Yellow
    }
}

# 2) Fallback: download installer and run
Write-Host "Falling back to direct download..." -ForegroundColor Cyan
$sdkVersion = "8.0.401"
$url = "https://dotnetcli.azureedge.net/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.exe"
$tempExe = Join-Path $env:TEMP "dotnet-sdk-8-win-x64.exe"

try {
    Write-Host "Downloading from $url ..." -ForegroundColor Gray
    Invoke-WebRequest -Uri $url -OutFile $tempExe -UseBasicParsing
} catch {
    # Try slightly older version if 8.0.401 is unavailable
    $sdkVersion = "8.0.204"
    $url = "https://dotnetcli.azureedge.net/dotnet/Sdk/$sdkVersion/dotnet-sdk-$sdkVersion-win-x64.exe"
    Write-Host "Trying $url ..." -ForegroundColor Gray
    Invoke-WebRequest -Uri $url -OutFile $tempExe -UseBasicParsing
}

if (-not (Test-Path $tempExe)) {
    Write-Host "ERROR: Download failed." -ForegroundColor Red
    exit 1
}

Write-Host "Running installer (installer window may appear)..." -ForegroundColor Cyan
Start-Process -FilePath $tempExe -ArgumentList "/install", "/quiet", "/norestart" -Wait
Remove-Item $tempExe -Force -ErrorAction SilentlyContinue

$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")
if (Test-DotNetInstalled) {
    Write-Host "Installation succeeded. Run 'dotnet --version' in a new PowerShell window." -ForegroundColor Green
    exit 0
}

Write-Host "Installation finished. Close and reopen PowerShell, then run 'dotnet --version'." -ForegroundColor Green
exit 0
