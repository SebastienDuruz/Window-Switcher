#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

export AVALONIA_TELEMETRY_OPTOUT="1"

dotnet run --project "${SCRIPT_DIR}/_build.csproj" -- --root "${REPO_ROOT}" "$@"
