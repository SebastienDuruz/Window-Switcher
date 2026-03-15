#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Build the Windows installer from Linux/macOS using dotnet publish + NSIS.

Usage:
  ./scripts/build-installer.sh [options]

Options:
  -c, --configuration <Release|Debug> Build configuration (default: Release)
  -r, --runtime <win-x64|win-arm64>   Runtime identifier (default: win-x64)
  -v, --version <x.y.z>               App version override (default: value from Directory.Build.props)
      --[no-]self-contained           Publish self-contained (default: framework-dependent)
      --publish-dir <path>            Dotnet publish output dir (default: scripts/artifacts/publish/<rid>)
      --out-dir <path>                Output directory (default: scripts/artifacts/installer)
      --makensis <path>               Use a specific makensis binary
  -h, --help                          Show help

Dependencies:
  - dotnet (SDK)
  - NSIS (makensis)
EOF
}

repo_root() {
  local script_dir
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  (cd "${script_dir}/.." && pwd)
}

require_cmd() {
  local cmd="$1"
  if ! command -v "$cmd" >/dev/null 2>&1; then
    echo "Missing dependency: '$cmd'." >&2
    exit 1
  fi
}

read_version_from_props() {
  local props_file="$1"

  if [[ ! -f "$props_file" ]]; then
    echo "Version file not found: $props_file" >&2
    exit 1
  fi

  local version
  version="$(sed -n 's:.*<WindowSwitcherVersion>\(.*\)</WindowSwitcherVersion>.*:\1:p' "$props_file" | head -n 1 | tr -d '[:space:]')"

  if [[ -z "$version" ]]; then
    echo "Unable to read WindowSwitcherVersion from: $props_file" >&2
    exit 1
  fi

  echo "$version"
}

resolve_makensis() {
  local provided_path="${1:-}"

  if [[ -n "$provided_path" ]]; then
    if [[ ! -x "$provided_path" ]]; then
      echo "makensis not found or not executable at: $provided_path" >&2
      exit 1
    fi

    echo "$provided_path"
    return
  fi

  if command -v makensis >/dev/null 2>&1; then
    command -v makensis
    return
  fi

  echo "Missing dependency: 'makensis'. Install NSIS or pass --makensis <path>." >&2
  exit 1
}

configuration="Release"
runtime="win-x64"
version=""
self_contained="false"
publish_dir=""
out_dir=""
makensis_path=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration) configuration="$2"; shift 2 ;;
    -r|--runtime) runtime="$2"; shift 2 ;;
    -v|--version) version="$2"; shift 2 ;;
    --self-contained) self_contained="true"; shift 1 ;;
    --no-self-contained) self_contained="false"; shift 1 ;;
    --publish-dir) publish_dir="$2"; shift 2 ;;
    --out-dir) out_dir="$2"; shift 2 ;;
    --makensis) makensis_path="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

case "$runtime" in
  win-x64|win-arm64) ;;
  *) echo "Unsupported runtime: $runtime (use win-x64 or win-arm64)." >&2; exit 2 ;;
esac

repo="$(repo_root)"
project="${repo}/src/WindowSwitcher/WindowSwitcher.csproj"
nsi="${repo}/scripts/assets/installer/WindowSwitcher.nsi"
version_props="${repo}/Directory.Build.props"

if [[ -z "$publish_dir" ]]; then
  publish_dir="${repo}/scripts/artifacts/publish/${runtime}"
fi
if [[ -z "$out_dir" ]]; then
  out_dir="${repo}/scripts/artifacts/installer"
fi
if [[ -z "$version" ]]; then
  version="$(read_version_from_props "$version_props")"
fi

require_cmd dotnet
makensis="$(resolve_makensis "$makensis_path")"

mkdir -p "$publish_dir" "$out_dir"

export AVALONIA_TELEMETRY_OPTOUT="1"

echo "Publishing (${configuration}, ${runtime}, self-contained=${self_contained})..." >&2
publish_msbuild_props=(
  -p:UsedAvaloniaProducts=
  -p:Version="$version"
  -p:PackageVersion="$version"
  -p:InformationalVersion="$version"
)

dotnet publish "$project" -c "$configuration" -r "$runtime" -o "$publish_dir" --self-contained "$self_contained" "${publish_msbuild_props[@]}"

if [[ -z "$(find "$publish_dir" -mindepth 1 -maxdepth 1 -print -quit)" ]]; then
  echo "dotnet publish produced no files in: $publish_dir" >&2
  exit 1
fi

installer_file="${out_dir}/WindowSwitcher-setup-${version}-${runtime}.exe"
publish_glob="${publish_dir%/}/*"

echo "Building NSIS installer: ${installer_file}" >&2
"$makensis" "-DAPP_VERSION=$version" "-DPUBLISH_DIR=$publish_dir" "-DPUBLISH_GLOB=$publish_glob" "-DOUT_FILE=$installer_file" "$nsi"

if [[ ! -f "$installer_file" ]]; then
  echo "Installer was not created at: $installer_file" >&2
  exit 1
fi

echo "Done: $installer_file"
