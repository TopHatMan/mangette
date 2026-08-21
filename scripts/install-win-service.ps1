#Requires -RunAsAdministrator
<#
.SYNOPSIS
  Publish Mangette (if needed) and install it as a Windows service that starts at boot.

  Library path, FlareSolverr URL, and listen port are optional. If omitted, this script
  reuses the existing Windows service environment and C:\Mangette\data\settings.json
  (and the library already stored in mangette.db). Pass -LibraryPath only on a first
  install before Settings has been saved.

.EXAMPLE
  .\scripts\install-win-service.ps1
.EXAMPLE
  .\scripts\install-win-service.ps1 -LibraryPath D:\Manga
.EXAMPLE
  .\scripts\install-win-service.ps1 -FlareSolverrUrl http://192.168.1.210:8181
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

function Get-ServiceEnvironmentMap([string]$Name) {
    $map = @{}
    $regPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$Name"
    if (-not (Test-Path $regPath)) { return $map }
    $lines = (Get-ItemProperty -Path $regPath -Name Environment -ErrorAction SilentlyContinue).Environment
    foreach ($line in @($lines)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $eq = $line.IndexOf("=")
        if ($eq -lt 1) { continue }
        $map[$line.Substring(0, $eq)] = $line.Substring($eq + 1)
    }
    return $map
}

function Get-SettingsObject([string]$HomeDir) {
    $path = Join-Path $HomeDir "data\settings.json"
    if (-not (Test-Path $path)) { return $null }
    try {
        return Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
    } catch {
        Write-Host "Could not parse $path — ignoring."
        return $null
    }
}

function Get-LibraryFromSqlite([string]$HomeDir) {
    $db = Join-Path $HomeDir "data\mangette.db"
    if (-not (Test-Path $db)) { return $null }
    $sqlite3 = Get-Command sqlite3 -ErrorAction SilentlyContinue
    if ($sqlite3) {
        try {
            $value = & sqlite3.exe -readonly $db "SELECT BasePath FROM FileLibraries ORDER BY LibraryName LIMIT 1;" 2>$null
            if ($value) { return ([string]$value).Trim() }
        } catch { }
    }
    return $null
}

Write-Host "Install dir: $InstallDir"

$existingEnv = Get-ServiceEnvironmentMap $ServiceName
$settings = Get-SettingsObject $InstallDir

if (-not $LibraryPath) {
    if ($existingEnv.ContainsKey("DOWNLOAD_LOCATION") -and $existingEnv["DOWNLOAD_LOCATION"]) {
        $LibraryPath = $existingEnv["DOWNLOAD_LOCATION"]
        Write-Host "Library path from existing service env: $LibraryPath"
    } elseif ($settings -and $settings.libraryPath) {
        $LibraryPath = [string]$settings.libraryPath
        Write-Host "Library path from settings.json: $LibraryPath"
    } elseif ($settings -and $settings.defaultLibraryPath) {
        $LibraryPath = [string]$settings.defaultLibraryPath
        Write-Host "Library path from settings.json defaultLibraryPath: $LibraryPath"
    } else {
        $fromDb = Get-LibraryFromSqlite $InstallDir
        if ($fromDb) {
            $LibraryPath = $fromDb
            Write-Host "Library path from mangette.db: $LibraryPath"
        }
    }
}

if (-not $FlareSolverrUrl) {
    if ($existingEnv.ContainsKey("FLARESOLVERR_URL") -and $existingEnv["FLARESOLVERR_URL"]) {
        $FlareSolverrUrl = $existingEnv["FLARESOLVERR_URL"]
    } elseif ($settings -and $settings.flareSolverrUrl) {
        $FlareSolverrUrl = [string]$settings.flareSolverrUrl
    }
}

$portWasDefault = $Port -eq 8585
if ($portWasDefault) {
    if ($settings -and $settings.listenPort -gt 0) {
        $Port = [int]$settings.listenPort
    } elseif ($existingEnv.ContainsKey("PORT") -and $existingEnv["PORT"]) {
        $parsed = 0
        if ([int]::TryParse($existingEnv["PORT"], [ref]$parsed) -and $parsed -gt 0) {
            $Port = $parsed
        }
    }
}

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
if ($LibraryPath) {
    Write-Host "Library:         $LibraryPath"
} else {
    Write-Host "Library:         using existing Settings / mangette.db (or $InstallDir\Manga on first run)"
}
Write-Host "Logs:            $InstallDir\data\logs\mangette.log"
Write-Host ""
Write-Host "Uninstall:  .\scripts\uninstall-win-service.ps1"
