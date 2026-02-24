#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Build Window Switcher as an AppImage (Linux).

Usage:
  ./scripts/build-appimage.sh [options]

Options:
  -c, --configuration <Release|Debug>   Build configuration (default: Release)
  -r, --runtime <linux-x64|linux-arm64> Runtime identifier (default: linux-x64)
  -v, --version <x.y.z>                 App version for the output name (default: 0.6.0)
      --[no-]self-contained             Publish self-contained (default: self-contained)
      --publish-dir <path>              Dotnet publish output dir (default: artifacts/publish/<rid>)
      --out-dir <path>                  Output directory (default: artifacts/appimage)
      --appimagetool <path>             Use a specific appimagetool (or AppImage) binary
  -h, --help                            Show help

Dependencies:
  - dotnet (SDK)
  - appimagetool (will be downloaded into artifacts/tools/ if missing)
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

download_file() {
  local url="$1"
  local out="$2"

  if command -v curl >/dev/null 2>&1; then
    curl -fL --retry 3 --retry-delay 1 -o "$out" "$url"
    return
  fi

  if command -v wget >/dev/null 2>&1; then
    wget -O "$out" "$url"
    return
  fi

  echo "Missing dependency: curl or wget (needed to download appimagetool)." >&2
  exit 1
}

ensure_appimagetool() {
  local desired_arch="$1" # x86_64 or aarch64
  local repo="$2"
  local provided_path="${3:-}"

  if [[ -n "$provided_path" ]]; then
    if [[ ! -f "$provided_path" ]]; then
      echo "appimagetool not found at: $provided_path" >&2
      exit 1
    fi
    echo "$provided_path"
    return
  fi

  if command -v appimagetool >/dev/null 2>&1; then
    command -v appimagetool
    return
  fi

  local tools_dir="${repo}/artifacts/tools"
  mkdir -p "$tools_dir"

  local appimagetool_path="${tools_dir}/appimagetool-${desired_arch}.AppImage"

  if [[ ! -f "$appimagetool_path" ]]; then
    local url="${APPIMAGETOOL_URL:-}"
    if [[ -z "$url" ]]; then
      case "$desired_arch" in
        x86_64) url="https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-x86_64.AppImage" ;;
        aarch64) url="https://github.com/AppImage/AppImageKit/releases/download/continuous/appimagetool-aarch64.AppImage" ;;
        *) echo "Unsupported arch for appimagetool download: $desired_arch" >&2; exit 1 ;;
      esac
    fi

    echo "Downloading appimagetool to: ${appimagetool_path}" >&2
    download_file "$url" "$appimagetool_path"
    chmod +x "$appimagetool_path"
  fi

  echo "$appimagetool_path"
}

configuration="Release"
runtime="linux-x64"
version="0.7.0"
self_contained="true"
publish_dir=""
out_dir=""
appimagetool_path=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration) configuration="$2"; shift 2 ;;
    -r|--runtime) runtime="$2"; shift 2 ;;
    -v|--version) version="$2"; shift 2 ;;
    --self-contained) self_contained="true"; shift 1 ;;
    --no-self-contained) self_contained="false"; shift 1 ;;
    --publish-dir) publish_dir="$2"; shift 2 ;;
    --out-dir) out_dir="$2"; shift 2 ;;
    --appimagetool) appimagetool_path="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

case "$runtime" in
  linux-x64) appimage_arch="x86_64" ;;
  linux-arm64) appimage_arch="aarch64" ;;
  *) echo "Unsupported runtime: $runtime (use linux-x64 or linux-arm64)." >&2; exit 2 ;;
esac

repo="$(repo_root)"
project="${repo}/src/WindowSwitcher/WindowSwitcher.csproj"
packaging_dir="${repo}/packaging/linux"

if [[ -z "$publish_dir" ]]; then
  publish_dir="${repo}/artifacts/publish/${runtime}"
fi
if [[ -z "$out_dir" ]]; then
  out_dir="${repo}/artifacts/appimage"
fi

require_cmd dotnet

mkdir -p "$publish_dir" "$out_dir"

export AVALONIA_TELEMETRY_OPTOUT="1"

echo "Publishing (${configuration}, ${runtime}, self-contained=${self_contained})..." >&2
publish_msbuild_props=( -p:UsedAvaloniaProducts= -p:Version="$version" -p:InformationalVersion="$version" )
if [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  publish_msbuild_props+=( -p:AssemblyVersion="${version}.0" -p:FileVersion="${version}.0" )
fi

dotnet publish "$project" -c "$configuration" -r "$runtime" -o "$publish_dir" --self-contained "$self_contained" "${publish_msbuild_props[@]}"

exe="${publish_dir}/WindowSwitcher"
if [[ ! -f "$exe" ]]; then
  echo "Published binary not found at: $exe" >&2
  exit 1
fi

appdir="${out_dir}/WindowSwitcher.AppDir"
rm -rf "$appdir"
mkdir -p "$appdir/usr/bin" "$appdir/usr/share/applications" "$appdir/usr/share/icons/hicolor/256x256/apps" "$appdir/usr/share/licenses/windowswitcher"

echo "Preparing AppDir at: $appdir" >&2
cp -a "${publish_dir}/." "$appdir/usr/bin/"

cp -a "${repo}/LICENSE" "$appdir/usr/share/licenses/windowswitcher/LICENSE"

cp -a "${repo}/src/WindowSwitcher/Assets/WS_logo.png" "$appdir/windowswitcher.png"
cp -a "$appdir/windowswitcher.png" "$appdir/.DirIcon"
cp -a "$appdir/windowswitcher.png" "$appdir/usr/share/icons/hicolor/256x256/apps/windowswitcher.png"

cp -a "${packaging_dir}/windowswitcher.desktop" "$appdir/windowswitcher.desktop"
cp -a "${packaging_dir}/windowswitcher.desktop" "$appdir/usr/share/applications/windowswitcher.desktop"

cp -a "${packaging_dir}/AppRun" "$appdir/AppRun"
chmod +x "$appdir/AppRun"

export ARCH="$appimage_arch"
export VERSION="$version"

appimagetool="$(ensure_appimagetool "$appimage_arch" "$repo" "$appimagetool_path")"

output_file="${out_dir}/WindowSwitcher-${version}-${runtime}.AppImage"
echo "Building AppImage: $output_file" >&2

if [[ "$appimagetool" == *.AppImage ]]; then
  "$appimagetool" --appimage-extract-and-run "$appdir" "$output_file"
else
  "$appimagetool" "$appdir" "$output_file"
fi

chmod +x "$output_file"
echo "Done: $output_file"
