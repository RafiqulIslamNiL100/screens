#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes Screens.App as a self-contained win-x64 build and packages it
    with NSIS into Screens-Setup-<version>.exe. Run from anywhere; paths are
    resolved relative to this script. See docs/RELEASE.md for the full
    release sequence (this script covers steps 2-3).
#>
param(
    [string]$Version = (Get-Content (Join-Path $PSScriptRoot "..\version.json") | ConvertFrom-Json).version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\Screens.App\Screens.App.csproj"
$publishDir = Join-Path $root "src\Screens.App\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"

Write-Host "Publishing Screens.App $Version (self-contained win-x64)..."
dotnet publish $project -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true /p:Version=$Version /p:AssemblyVersion="$Version.0" /p:FileVersion="$Version.0"

if (-not (Test-Path (Join-Path $publishDir "Screens.exe"))) {
    throw "Publish did not produce Screens.exe at $publishDir"
}

Write-Host "Running makensis..."
$nsis = Join-Path $PSScriptRoot "Screens.nsi"
makensis "/DVERSION=$Version" $nsis

$installer = Join-Path $PSScriptRoot "Screens-Setup-$Version.exe"
if (-not (Test-Path $installer)) {
    throw "makensis did not produce $installer"
}

$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLower()
Write-Host ""
Write-Host "Built: $installer"
Write-Host "SHA-256: $hash"
