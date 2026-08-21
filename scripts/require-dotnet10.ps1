$ErrorActionPreference = "Stop"

function Get-DotnetSdks {
    & dotnet --list-sdks 2>$null
}

$sdks = Get-DotnetSdks
$has10 = $false
if ($sdks) {
    foreach ($line in $sdks) {
        if ($line -match '^10\.') { $has10 = $true; break }
    }
}

if (-not $has10) {
    Write-Host ""
    Write-Host "Mangette requires the .NET 10 SDK. This machine does not have it."
    Write-Host "dotnet currently reports:"
    Write-Host ""
    try { & dotnet --info } catch { Write-Host $_ }
    Write-Host ""
    Write-Host "Installed SDKs:"
    if ($sdks) { $sdks | ForEach-Object { Write-Host "  $_" } } else { Write-Host "  (none found)" }
    Write-Host ""
    Write-Host "Install .NET 10 SDK, then close and reopen PowerShell:"
    Write-Host "  winget install Microsoft.DotNet.SDK.10 --source winget"
    Write-Host "  or https://dotnet.microsoft.com/download/dotnet/10.0"
    Write-Host ""
    Write-Host "Then confirm:"
    Write-Host "  dotnet --list-sdks"
    Write-Host "You should see a 10.x line. If 5.0 is still first, PATH is wrong:"
    Write-Host "  C:\Program Files\dotnet  must come before any ...\sdk\5.0... folder."
    Write-Host ""
    exit 1
}
