#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Build Window Switcher release artifacts from Linux in one command.
By default, this builds:
  - Windows installer (.exe)
  - Linux AppImage (.AppImage)

Usage:
  ./scripts/build-artifacts.sh [options]

Options:
  -c, --configuration <Release|Debug>     Build configuration (default: Release)
  -v, --version <x.y.z>                   App version override (default: value from Directory.Build.props)
      --[no-]self-contained               Publish self-contained for both artifacts (default: framework-dependent)
      --windows-runtime <win-x64|win-arm64>
                                          Windows runtime (default: win-x64)
      --linux-runtime <linux-x64|linux-arm64>
                                          Linux runtime (default: linux-x64)
      --windows-publish-dir <path>        Windows dotnet publish output dir
      --linux-publish-dir <path>          Linux dotnet publish output dir
      --installer-out-dir <path>          Windows installer output directory
      --appimage-out-dir <path>           AppImage output directory
      --makensis <path>                   Use a specific makensis binary
      --appimagetool <path>               Use a specific appimagetool binary
      --skip-installer                    Build only the Linux AppImage
      --skip-appimage                     Build only the Windows installer
  -h, --help                              Show help
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

validate_provided_executable() {
  local name="$1"
  local path="$2"

  if [[ ! -f "$path" || ! -x "$path" ]]; then
    echo "${name} not found or not executable at: $path" >&2
    exit 1
  fi
}

configuration="Release"
version=""
self_contained="false"
windows_runtime="win-x64"
linux_runtime="linux-x64"
windows_publish_dir=""
linux_publish_dir=""
installer_out_dir=""
appimage_out_dir=""
makensis_path=""
appimagetool_path=""
build_installer="true"
build_appimage="true"

while [[ $# -gt 0 ]]; do
  case "$1" in
    -c|--configuration) configuration="$2"; shift 2 ;;
    -v|--version) version="$2"; shift 2 ;;
    --self-contained) self_contained="true"; shift 1 ;;
    --no-self-contained) self_contained="false"; shift 1 ;;
    --windows-runtime) windows_runtime="$2"; shift 2 ;;
    --linux-runtime) linux_runtime="$2"; shift 2 ;;
    --windows-publish-dir) windows_publish_dir="$2"; shift 2 ;;
    --linux-publish-dir) linux_publish_dir="$2"; shift 2 ;;
    --installer-out-dir) installer_out_dir="$2"; shift 2 ;;
    --appimage-out-dir) appimage_out_dir="$2"; shift 2 ;;
    --makensis) makensis_path="$2"; shift 2 ;;
    --appimagetool) appimagetool_path="$2"; shift 2 ;;
    --skip-installer) build_installer="false"; shift 1 ;;
    --skip-appimage) build_appimage="false"; shift 1 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown argument: $1" >&2; usage; exit 2 ;;
  esac
done

if [[ "$build_installer" == "false" && "$build_appimage" == "false" ]]; then
  echo "Nothing to build: both --skip-installer and --skip-appimage were specified." >&2
  exit 2
fi

repo="$(repo_root)"
installer_script="${repo}/scripts/build-installer.sh"
appimage_script="${repo}/scripts/build-appimage.sh"

installer_args=( --configuration "$configuration" --runtime "$windows_runtime" )
appimage_args=( --configuration "$configuration" --runtime "$linux_runtime" )

if [[ -n "$version" ]]; then
  installer_args+=( --version "$version" )
  appimage_args+=( --version "$version" )
fi

if [[ "$self_contained" == "true" ]]; then
  installer_args+=( --self-contained )
  appimage_args+=( --self-contained )
else
  installer_args+=( --no-self-contained )
  appimage_args+=( --no-self-contained )
fi

if [[ -n "$windows_publish_dir" ]]; then
  installer_args+=( --publish-dir "$windows_publish_dir" )
fi
if [[ -n "$linux_publish_dir" ]]; then
  appimage_args+=( --publish-dir "$linux_publish_dir" )
fi
if [[ -n "$installer_out_dir" ]]; then
  installer_args+=( --out-dir "$installer_out_dir" )
fi
if [[ -n "$appimage_out_dir" ]]; then
  appimage_args+=( --out-dir "$appimage_out_dir" )
fi
if [[ -n "$makensis_path" ]]; then
  installer_args+=( --makensis "$makensis_path" )
fi
if [[ -n "$appimagetool_path" ]]; then
  appimage_args+=( --appimagetool "$appimagetool_path" )
fi

require_cmd dotnet

if [[ "$build_installer" == "true" ]]; then
  if [[ -n "$makensis_path" ]]; then
    validate_provided_executable "makensis" "$makensis_path"
  elif ! command -v makensis >/dev/null 2>&1; then
    echo "Missing dependency for Windows installer: 'makensis'." >&2
    echo "Install NSIS, pass --makensis <path>, or rerun with --skip-installer to build only the AppImage." >&2
    exit 1
  fi
fi

if [[ "$build_appimage" == "true" && -n "$appimagetool_path" ]]; then
  validate_provided_executable "appimagetool" "$appimagetool_path"
fi

if [[ "$build_installer" == "true" ]]; then
  "$installer_script" "${installer_args[@]}"
fi

if [[ "$build_appimage" == "true" ]]; then
  "$appimage_script" "${appimage_args[@]}"
fi
