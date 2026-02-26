<p align="center">
  <img align="middle" width="300" src="./WindowSwitcher/Assets/WS_logo.png">
</p>

# Window Switcher

**Window Switcher** is an **open-source** application that enables users to display **live previews** of selected open windows, with the ability to move, resize, and click to focus windows.

It offers a flexible configuration system based on **prefix-based filters and blacklists**, allowing precise control over which windows are displayed.

This software is inspired by [**eve-o-preview**](https://github.com/EveOPlus/eve-o-preview), but designed for a broader range of use cases.
The primary goal is to provide an **easy and efficient way to multibox** different game clients.

This software **doesn't modify game clients**.

## Main features:

- 🔍 **Live previews** of selected open windows
- ⚙️ **Configurable filters** using prefixes and blacklists
- 🖱️ **Click to focus** the window
- 🧲 **Focus on hover** (optional)
- 🖊️ **Rename** windows

## Compatibility

- ✅ **Windows** (fully supported)
- 🧪 **Linux** (experimental)
- ⏳ **macOS** (not yet implemented)

## Limitations

- 🖵 **Fullscreen applications** not supported

## How it works

- **Windows:** uses DWM thumbnails for smooth live previews.
- **Linux (X11):** uses periodic screenshots (requires ImageMagick `import`) and `wmctrl` to focus/rename windows.
- **Linux (Wayland):** supports PipeWire stream preview by matching `wmctrl` window ids to PipeWire nodes.

## Roadmap

- [x] Windows support
- [x] Basic Linux support
- [x] Advanced customization (access settings from the application)
- [ ] UI enhancements
- [ ] Better support for Linux
- [ ] macOS implementation

## Demo

<details open>
  <summary>v0.4.0</summary>

### Features

- Add a window to configure **settings**
- Add the ability to **rename** windows
    - keep track of different settings for clients like **World of Warcraft**

| Main window                                  | Prefix window                                  |
| -------------------------------------------- | ---------------------------------------------- |
| ![Screenshot 1](./Demo/0.4.0/mainwindow.png) | ![Screenshot 2](./Demo/0.4.0/prefixwindow.png) |

| Live preview                                 | Settings / Rename                                                                                |
| -------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| ![Screenshot 3](./Demo/0.4.0/thumbnails.png) | ![Screenshot 4](./Demo/0.4.0/settingswindows.png) ![Screenshot 4](./Demo/0.4.0/renamewindow.png) |

🎥 Example with **Eve Online**, **World of Warcraft** and **Project Gorgon** clients :

[![Watch the video](https://img.youtube.com/vi/hXvS_n32jaQ/0.jpg)](https://youtu.be/hXvS_n32jaQ)

</details>
<details>
  <summary>v0.1.0</summary>

| Main window                          | Prefix window                           |
| ------------------------------------ | --------------------------------------- |
| ![Screenshot 1](./Demo/settings.png) | ![Screenshot 2](./Demo/mainwindows.png) |

| Live preview                           |
| -------------------------------------- |
| ![Screenshot 3](./Demo/thumbnails.png) |

🎥 Example with **Eve Online**, **World of Warcraft** and **Guild Wars 2** clients :

[![Watch the video](https://img.youtube.com/vi/9oif2M7rryQ/0.jpg)](https://youtu.be/9oif2M7rryQ)

</details>

## Installation

Download the latest release [here](https://github.com/SebastienDuruz/Window-Switcher/releases)

### Quick start

**Windows**

- Download and run the `WindowSwitcher-Setup-*.exe` installer from the releases page.

**Linux**

- Download the `*.AppImage` from the releases page.
- Make it executable and run it:
    - `chmod +x WindowSwitcher-*.AppImage`
    - `./WindowSwitcher-*.AppImage`

## Build from source

From the repo root:

- Build: `dotnet build Window-Switcher.sln`
- Run: `dotnet run --project WindowSwitcher/WindowSwitcher.csproj`
- Tester project: `dotnet run --project WindowSwitcherTester/WindowSwitcherTester.csproj`

## Build Windows installer (scripted)

Prerequisite: install NSIS (so `makensis.exe` is available).

From the repo root:

`pwsh ./scripts/build-installer.ps1 -Version 0.6.0`

(Works in Windows PowerShell too: `powershell ./scripts/build-installer.ps1 -Version 0.6.0`.)

## Build Linux AppImage (scripted)

From the repo root:

`./scripts/build-appimage.sh -v 0.6.0`

Output: `./artifacts/appimage/WindowSwitcher-0.6.0-linux-x64.AppImage`

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

1. Launch Window Switcher.
2. Configure which windows to preview:
    - Prefix filter: only show windows with specific names.
    - Blacklist filter: exclude unwanted windows.
3. Adjust the preview size and position.
4. (Optional) Enable **Focus on hover** from the **Settings** window.
5. Enjoy !

## Configuration

Settings are persisted to `config.json` under the app data folder:

- Windows: `%APPDATA%\\WindowSwitcher\\config.json`
- Linux: `~/.config/WindowSwitcher/config.json` (typically)

## License

This project is licensed under the [GPL3 License](LICENSE).

## Donations

If you find **Window Switcher** useful and would like to support its development, consider [buying me a coffee](https://buymeacoffee.com/sebastienduruz) ☕

Your support helps keep this project alive and motivates further improvements. Thank you! 🙌
