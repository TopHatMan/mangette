#!/usr/bin/env bash
# Run on the Debian VirtualBox VM (192.168.1.210).
# Publishes FlareSolverr on all interfaces at port 8181 so Windows Mangette can use
# http://192.168.1.210:8181
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

export FLARESOLVERR_PORT="${FLARESOLVERR_PORT:-8181}"

if ! command -v docker >/dev/null 2>&1; then
  echo "Install Docker Engine on this VM first:"
  echo "  https://docs.docker.com/engine/install/debian/"
  exit 1
fi

if ! docker compose version >/dev/null 2>&1 && ! docker-compose version >/dev/null 2>&1; then
  echo "Install the Docker Compose plugin: apt install docker-compose-plugin"
  exit 1
fi

if docker compose version >/dev/null 2>&1; then
  COMPOSE=(docker compose)
else
  COMPOSE=(docker-compose)
fi

"${COMPOSE[@]}" up -d

if command -v ufw >/dev/null 2>&1; then
  if ufw status 2>/dev/null | grep -qi "Status: active"; then
    echo "Opening ufw tcp/${FLARESOLVERR_PORT}..."
    ufw allow "${FLARESOLVERR_PORT}/tcp" comment "Mangette FlareSolverr" || true
  fi
fi

echo
echo "FlareSolverr is on host port ${FLARESOLVERR_PORT} (docker network_mode: host)"
echo "  this VM:  http://127.0.0.1:${FLARESOLVERR_PORT}"
echo "  Windows:  http://192.168.1.210:${FLARESOLVERR_PORT}"
echo
echo "VirtualBox must use a Bridged adapter (or NAT port-forward TCP ${FLARESOLVERR_PORT})."
echo "On Windows Mangette: Settings -> Cloudflare bypass -> save that URL, then Test FlareSolverr."
echo
echo "Local check:"
echo "  curl -sS -o /dev/null -w '%{http_code}\\n' http://127.0.0.1:${FLARESOLVERR_PORT}/"
