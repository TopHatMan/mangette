#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="${ROOT}/dist/linux-x64"
cd "$ROOT"

dotnet publish API/API.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true \
  -p:SkipFrontend="${SKIP_FRONTEND:-true}" \
  -o "$OUT"

echo
echo "Published to $OUT"
echo "Run:  $OUT/API"
echo "Open: http://localhost:6531"
