#Requires -RunAsAdministrator
param(
    [string]$ServiceName = "Mangette",
    [switch]$RemoveFiles,
    [string]$InstallDir = "C:\Mangette"
)

$ErrorActionPreference = "Stop"
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Removed service $ServiceName"
} else {
    Write-Host "Service $ServiceName was not installed."
}

Get-NetFirewallRule -DisplayName "Mangette HTTP *" -ErrorAction SilentlyContinue | Remove-NetFirewallRule

if ($RemoveFiles -and (Test-Path $InstallDir)) {
    Remove-Item -Recurse -Force $InstallDir
    Write-Host "Deleted $InstallDir"
}
