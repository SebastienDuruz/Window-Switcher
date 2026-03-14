<p align="center">
  <img align="middle" width="300" src="./src/WindowSwitcher/Assets/WS_logo.png">
</p>

# Window Switcher

**Window Switcher** is an open-source desktop app that creates small always-on-top preview windows for selected applications.

It is designed for fast window switching and multibox workflows (games, tools, multi-client setups), without modifying target applications.

Inspired by [**eve-o-preview**](https://github.com/EveOPlus/eve-o-preview), the project focuses on broader use cases and cross-platform support.

## What you can do

- Display floating previews for selected windows
- Bring the original window to foreground by clicking a preview
- Optionally focus windows on mouse hover
- Filter displayed windows with whitelist prefixes
- Exclude windows with blacklist entries
- Temporarily hide a window for the current session (temp blacklist)
- Rename a window title directly from the UI
- Persist floating window size/position per window
- Tune preview behavior and highlight color from Settings
- Inspect runtime/OS/dependency status from the About window

## Compatibility

- ✅ **Windows**
- ✅ **Linux** (X11 + Wayland)
- ⏳ **macOS** (not implemented)

## Limitations

- 🖵 **Fullscreen applications** not supported

## How it works

- **Windows:** native DWM thumbnail previews are used in floating windows.
- **Linux (X11):** screenshots are captured via ImageMagick `import`, and window actions use `wmctrl`.
- **Linux (Wayland):** PipeWire preview is used when dependencies are available; otherwise it falls back to screenshot mode.

## Typical workflow

1. Launch Window Switcher.
2. Open `Settings > Configure prefixes` and add entries to the whitelist.
3. Open `Settings > Configure blacklist` to exclude titles you do not want to see.
4. Floating previews are created for matching windows.
5. Click a preview to focus the original window, or enable `Focus on hover`.
6. Right-click a preview (or list item) to blacklist, temp-blacklist, or rename a window.

## Demo

<details open>
  <summary>v0.7.0</summary>

  🎥 Example with 3 **World of Warcraft** clients, on Arch Linux KDE (Wayland)
  
  [![Watch the video](https://img.youtube.com/vi/QQTOkl0HD9s/0.jpg)](https://youtu.be/QQTOkl0HD9s)

</details>

<details>
  <summary>v0.4.0</summary>

### Features

- Add a window to configure **settings**
- Add the ability to **rename** windows
    - keep track of different settings for clients like **World of Warcraft**

| Main window                               | Prefix window                                  |
|-------------------------------------------|------------------------------------------------|
| ![Screenshot 1](./docs/0.4.0/mainwindow.png) | ![Screenshot 2](./docs/0.4.0/prefixwindow.png) |

| Live preview                                 | Settings / Rename                                                                                |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| ![Screenshot 3](./docs/0.4.0/thumbnails.png) | ![Screenshot 4](./docs/0.4.0/settingswindows.png) ![Screenshot 4](./docs/0.4.0/renamewindow.png) |

🎥 Example with **Eve Online**, **World of Warcraft** and **Project Gorgon** clients :

[![Watch the video](https://img.youtube.com/vi/hXvS_n32jaQ/0.jpg)](https://youtu.be/hXvS_n32jaQ)

</details>

## Requirements

- Runtime: Windows or Linux
- Source build: .NET SDK 10 (`net10.0`)
- Linux preview/focus features require external tools (see Linux dependencies below)

## Installation

Download the latest release [here](https://github.com/SebastienDuruz/Window-Switcher/releases)

### Quick start

**Windows**

- Download and run the `WindowSwitcher-setup-*.exe` installer from the releases page.

**Linux**

- Download the `*.AppImage` from the releases page.
- Make it executable and run it:
    - `chmod +x WindowSwitcher-*.AppImage`
    - `./WindowSwitcher-*.AppImage`

## Build from source

From the repo root:

- Build: `dotnet build Window-Switcher.sln`
- Run: `dotnet run --project src/WindowSwitcher/WindowSwitcher.csproj`
- Test: `dotnet test src/WindowSwitcher.Tests/WindowSwitcher.Tests.csproj`

The application version is centralized in `./Directory.Build.props` via `WindowSwitcherVersion`.

## Build Windows installer (scripted)

Prerequisite: install NSIS (so `makensis` is available).

From the repo root:

`pwsh ./scripts/build-installer.ps1`

The script reads the version from `./Directory.Build.props` by default. Use `-Version` only to override it for a specific build.

(Works in Windows PowerShell too: `powershell ./scripts/build-installer.ps1`.)

From Linux/macOS, you can also use:

`./scripts/build-installer.sh`

## Build Linux AppImage (scripted)

From the repo root:

`./scripts/build-appimage.sh`

The script reads the version from `./Directory.Build.props` by default. Use `-v` only to override it for a specific build.

Output: `./artifacts/appimage/WindowSwitcher-<version>-linux-x64.AppImage`

## Build all artifacts from Linux

From the repo root:

`./scripts/build-artifacts.sh`

By default, this produces both:

- `./artifacts/installer/WindowSwitcher-setup-<version>-win-x64.exe`
- `./artifacts/appimage/WindowSwitcher-<version>-linux-x64.AppImage`

You can also target a single artifact from the wrapper:

- `./scripts/build-artifacts.sh --skip-installer` builds only the Linux AppImage
- `./scripts/build-artifacts.sh --skip-appimage` builds only the Windows installer

Prerequisites on Linux:

- `.NET SDK`
- `NSIS` (`makensis`) when building the Windows installer
- `appimagetool` in `PATH`, or let the AppImage script download it automatically

### Linux dependencies

Make sure your system has:

- [`wmctrl`](https://linux.die.net/man/1/wmctrl) (list/focus/rename windows)
- ImageMagick [`import`](https://linux.die.net/man/1/import) (screenshots for live preview)
- [`gst-launch-1.0`](https://gstreamer.freedesktop.org/) + `pipewiresrc` plugin (PipeWire video stream)
- [`pw-dump`](https://pipewire.pages.freedesktop.org/pipewire/page_man_pw-dump_1.html) (PipeWire node discovery and matching)
- [`gdbus`](https://manpages.ubuntu.com/manpages/jammy/man1/gdbus.1.html) (optional, only if portal fallback is enabled)

Install examples (depends on your distro):

- Debian/Ubuntu: `sudo apt install wmctrl imagemagick`
- Arch: `sudo pacman -S wmctrl imagemagick`
- Fedora: `sudo dnf install wmctrl ImageMagick`

Wayland PipeWire packages (examples):

- Debian/Ubuntu: `sudo apt install gstreamer1.0-tools gstreamer1.0-pipewire pipewire-bin libglib2.0-bin`
- Arch: `sudo pacman -S gst-plugin-pipewire gstreamer pipewire glib2`
- Fedora: `sudo dnf install gstreamer1 pipewire-gstreamer pipewire-utils glib2`

## Usage

### Filters behavior

- **Whitelist prefixes:** a window is shown when its title contains at least one configured prefix (case-insensitive).
- **Blacklist:** exact title matches are excluded (case-insensitive).
- **Temp blacklist:** hides a window by ID for the current session only.

### Floating preview behavior

- Drag preview windows when `Move windows` is enabled.
- Resize previews when `Resize windows` is enabled.
- With `Fixed size`, all previews share configured width/height.
- Preview position and size are restored per window key on next launch.

### Settings available in-app

- `Disable previews`
- `Move windows`
- `Resize windows`
- `Focus on hover`
- `Start minimized`
- `Window decorations` (Linux policy)
- `Fixed size` + `Width/Height`
- `Highlight color`

### In-app diagnostics

`Help > About` shows:

- App version
- OS / .NET runtime / architecture
- UI backend
- Config file path
- Active preview mode
- Linux dependency status

## Configuration

Settings are persisted to `config.json` under the app data folder:

- Windows: `%APPDATA%\\WindowSwitcher\\config.json`
- Linux: `~/.config/WindowSwitcher/config.json` (typically)

## License

This project is licensed under the [GPL3 License](LICENSE).

## Donations

If you find **Window Switcher** useful and would like to support its development, consider [buying me a coffee](https://buymeacoffee.com/sebastienduruz) ☕

Your support helps keep this project alive and motivates further improvements. Thank you! 🙌
