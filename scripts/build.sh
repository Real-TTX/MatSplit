#!/usr/bin/env bash
# ---------------------------------------------------------------------------
# Builds the MatSplit container image (version "local-<yyyyMMdd>") and
# (re)deploys the development stack:
#   app            -> http://localhost:4774
#   sqlite browser -> http://localhost:4775
#
# Usage: scripts/build.sh [--release] [--no-cache] [--build-only] [--follow]
# ---------------------------------------------------------------------------
set -euo pipefail

CONFIGURATION="Debug"
NO_CACHE=""
BUILD_ONLY="0"
FOLLOW="0"

usage() {
  sed -n '2,10p' "$0" | sed 's/^# \{0,1\}//'
  exit "${1:-0}"
}

while [ $# -gt 0 ]; do
  case "$1" in
    --release)    CONFIGURATION="Release" ;;
    --debug)      CONFIGURATION="Debug" ;;
    --no-cache)   NO_CACHE="--no-cache" ;;
    --build-only) BUILD_ONLY="1" ;;
    --follow|-f)  FOLLOW="1" ;;
    -h|--help)    usage 0 ;;
    *) echo "Unknown option: $1" >&2; usage 1 ;;
  esac
  shift
done

command -v docker >/dev/null 2>&1 || { echo "docker was not found in PATH." >&2; exit 1; }

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(dirname -- "$SCRIPT_DIR")"
COMPOSE_FILE="docker-compose.dev.yml"

cd "$REPO_ROOT"
[ -f "$COMPOSE_FILE" ] || { echo "Missing compose file: $REPO_ROOT/$COMPOSE_FILE" >&2; exit 1; }

STAMP="$(date +%Y%m%d)"
VERSION="local-${STAMP}"
BUILD_DATE="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
VCS_REF="$(git rev-parse --short HEAD 2>/dev/null || echo unknown)"

echo "MatSplit build"
echo "  repo          : $REPO_ROOT"
echo "  version       : $VERSION"
echo "  configuration : $CONFIGURATION"

echo
echo "==> Build image matsplit:${VERSION}"
docker build \
  --file Dockerfile \
  --tag "matsplit:${VERSION}" \
  --tag "matsplit:local" \
  --build-arg "BUILD_CONFIGURATION=${CONFIGURATION}" \
  --build-arg "APP_VERSION=${VERSION}" \
  --build-arg "BUILD_DATE=${BUILD_DATE}" \
  --build-arg "VCS_REF=${VCS_REF}" \
  ${NO_CACHE} \
  .

if [ "$BUILD_ONLY" = "1" ]; then
  echo
  echo "Image matsplit:${VERSION} built. Stack untouched (--build-only)."
  exit 0
fi

echo
echo "==> Deploy dev stack"
MATSPLIT_VERSION="$VERSION" docker compose -f "$COMPOSE_FILE" up -d --build --remove-orphans

docker image tag matsplit:dev "matsplit:${VERSION}" >/dev/null 2>&1 || true

echo
echo "==> Stack status"
docker compose -f "$COMPOSE_FILE" ps

echo
echo "MatSplit    -> http://localhost:4774"
echo "SQLite view -> http://localhost:4775"
echo "Logs        -> docker compose -f ${COMPOSE_FILE} logs -f msbi"

if [ "$FOLLOW" = "1" ]; then
  docker compose -f "$COMPOSE_FILE" logs -f msbi
fi
