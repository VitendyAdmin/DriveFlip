#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes DriveFlip and builds the Inno Setup installer.

.DESCRIPTION
    1. Runs 'dotnet publish' as self-contained x64
    2. Invokes Inno Setup compiler (ISCC) to produce the installer EXE

.PARAMETER Configuration
    Build configuration. Default: Release

.EXAMPLE
    .\Installer\Build-Installer.ps1
#>
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path $PSScriptRoot -Parent
$PublishDir  = Join-Path $ProjectRoot "publish"
$IssFile     = Join-Path $PSScriptRoot "DriveFlip.iss"
$OutputDir   = Join-Path $PSScriptRoot "Output"

# --- Step 1: Publish ---
Write-Host "`n=== Publishing DriveFlip ($Configuration, self-contained, x64) ===" -ForegroundColor Cyan

if (Test-Path $PublishDir) {
    Remove-Item $PublishDir -Recurse -Force
}

dotnet publish "$ProjectRoot\DriveFlip.csproj" `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=false `
    -p:PublishTrimmed=false `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
    exit 1
}

Write-Host "Published to: $PublishDir" -ForegroundColor Green

# --- Step 2: Compile installer ---
Write-Host "`n=== Building Inno Setup installer ===" -ForegroundColor Cyan

# Find ISCC.exe (system-wide and per-user install locations)
$IsccPaths = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe",
    "${env:LocalAppData}\Programs\Inno Setup 6\ISCC.exe"
)

$Iscc = $IsccPaths | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $Iscc) {
    Write-Warning "Inno Setup 6 (ISCC.exe) not found. Publish output is ready at: $PublishDir"
    Write-Warning "Install Inno Setup 6 from https://jrsoftware.org/isdl.php then re-run this script."
    exit 0
}

& $Iscc $IssFile

if ($LASTEXITCODE -ne 0) {
    Write-Error "ISCC failed with exit code $LASTEXITCODE"
    exit 1
}

$Installer = Get-ChildItem $OutputDir -Filter "*.exe" | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Host "`nInstaller built: $($Installer.FullName)" -ForegroundColor Green
Write-Host "Size: $([math]::Round($Installer.Length / 1MB, 1)) MB" -ForegroundColor Green
