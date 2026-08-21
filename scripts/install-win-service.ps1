#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Publish Mangette (if needed) and install it as a Windows service that starts at boot.

.EXAMPLE
  .\scripts\install-win-service.ps1 -FlareSolverrUrl http://192.168.1.210:8191 -LibraryPath D:\Manga
#>
param(
    [string]$InstallDir = "C:\Mangette",
    [string]$FlareSolverrUrl = "",
    [string]$LibraryPath = "",
    [int]$Port = 8585,
    [string]$ServiceName = "Mangette",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
& (Join-Path $PSScriptRoot "require-dotnet10.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$Root = Split-Path -Parent $PSScriptRoot
$ExeName = "Mangette.exe"
$InstallDir = [System.IO.Path]::GetFullPath($InstallDir)
$ExePath = Join-Path $InstallDir $ExeName

Write-Host "Install dir: $InstallDir"

if (-not $SkipPublish -or -not (Test-Path $ExePath)) {
    Write-Host "Publishing win-x64 (self-contained, UI already in wwwroot)..."
    $env:SKIP_FRONTEND = "true"
    & (Join-Path $PSScriptRoot "publish-win-x64.ps1")
    $Published = Join-Path $Root "dist\win-x64\$ExeName"
    if (-not (Test-Path $Published)) {
        throw "Publish did not produce $Published"
    }
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Copy-Item -Force -Recurse (Join-Path $Root "dist\win-x64\*") $InstallDir
}

if (-not (Test-Path $ExePath)) {
    throw "Mangette.exe not found at $ExePath"
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing $ServiceName service..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

$binPath = "`"$ExePath`""
Write-Host "Creating service $ServiceName -> $ExePath"
New-Service -Name $ServiceName `
    -BinaryPathName $binPath `
    -DisplayName "Mangette" `
    -Description "Mangette manga downloader (API + UI on port $Port)" `
    -StartupType Automatic | Out-Null

sc.exe config $ServiceName start= delayed-auto | Out-Null
sc.exe failure $ServiceName reset= 86400 actions= restart/15000/restart/30000/restart/60000 | Out-Null

$envLines = @(
    "MANGETTE_HOME=$InstallDir",
    "PORT=$Port"
)
if ($FlareSolverrUrl) {
    $envLines += "FLARESOLVERR_URL=$FlareSolverrUrl"
}
if ($LibraryPath) {
    $LibraryPath = [System.IO.Path]::GetFullPath($LibraryPath)
    $envLines += "DOWNLOAD_LOCATION=$LibraryPath"
}

$regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
New-ItemProperty -Path $regPath -Name Environment -PropertyType MultiString -Value $envLines -Force | Out-Null

$ruleName = "Mangette HTTP $Port"
Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port | Out-Null

Start-Service -Name $ServiceName
Start-Sleep -Seconds 2
Get-Service -Name $ServiceName | Format-List Name, Status, StartType

Write-Host ""
Write-Host "Mangette service installed (delayed auto-start)."
Write-Host "UI:              http://localhost:$Port"
Write-Host "Cloudflare:      built-in Chromium (Chrome/Edge if installed)"
if ($FlareSolverrUrl) { Write-Host "FlareSolverr:    $FlareSolverrUrl" }
if ($LibraryPath) { Write-Host "Library:         $LibraryPath" }
Write-Host "Logs:            $InstallDir\data\logs\mangette.log"
Write-Host ""
Write-Host "Uninstall:  .\scripts\uninstall-win-service.ps1"
