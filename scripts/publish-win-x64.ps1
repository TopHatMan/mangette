$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot "require-dotnet10.ps1")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$Out = Join-Path $Root "dist\win-x64"

dotnet publish (Join-Path $Root "API\API.csproj") `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -p:SkipFrontend="$(if ($env:SKIP_FRONTEND) { $env:SKIP_FRONTEND } else { 'true' })" `
  -p:OpenApiGenerateDocumentsOnBuild=false `
  -o $Out

Write-Host ""
Write-Host "Published to $Out"
Write-Host "Run:  $Out\Mangette.exe"
Write-Host "Open: http://localhost:8585"
