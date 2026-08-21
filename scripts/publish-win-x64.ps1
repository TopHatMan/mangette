$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
$Out = Join-Path $Root "dist\win-x64"

dotnet publish (Join-Path $Root "API\API.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:SkipFrontend="$(if ($env:SKIP_FRONTEND) { $env:SKIP_FRONTEND } else { 'true' })" `
  -o $Out

Write-Host ""
Write-Host "Published to $Out"
Write-Host "Run:  $Out\API.exe"
Write-Host "Open: http://localhost:6531"
