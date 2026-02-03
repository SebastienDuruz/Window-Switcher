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
- 🖊️ **Rename** windows

## Compatibility
  - ✅ **Windows** (fully supported)
  - 🧪 **Linux** (experimental)
  - ⏳ **MacOS** (not yet implemented)

## Limitations
- 🖵 **Fullscreen applications** not supported

## Roadmap
- [x] Windows support
- [x] Basic Linux support
- [ ] Advanced customization (access settings from the application)
- [ ] UI enhancements
- [ ] Better support for Linux
- [ ] MacOS implementation

## Demo

<details open>
  <summary>v0.4.0</summary>

  ### Features
  - Add a window to configure **settings**
  - Add the ability to **rename** windows
    - keep track of different settings for clients like **World of Warcraft**
  
| Main window | Prefix window |
|-----------|-----------|
| ![Screenshot 1](./Demo/0.4.0/mainwindow.png) | ![Screenshot 2](./Demo/0.4.0/prefixwindow.png) |

| Live preview | Settings / Rename |
|----------- |----------- |
| ![Screenshot 3](./Demo/0.4.0/thumbnails.png) | ![Screenshot 4](./Demo/0.4.0/settingswindows.png) ![Screenshot 4](./Demo/0.4.0/renamewindow.png) |

🎥 Example with **Eve Online**, **World of Warcraft** and **Project Gorgon** clients :

 [![Watch the video](https://img.youtube.com/vi/hXvS_n32jaQ/0.jpg)](https://youtu.be/hXvS_n32jaQ)
</details>
<details>
  <summary>v0.1.0</summary>

  | Main window | Prefix window |
  |-----------|-----------|
  | ![Screenshot 1](./Demo/settings.png) | ![Screenshot 2](./Demo/mainwindows.png) |

  | Live preview |
  |----------- |
  | ![Screenshot 3](./Demo/thumbnails.png) |

  🎥 Example with **Eve Online**, **World of Warcraft** and **Guild Wars 2** clients :

 
  [![Watch the video](https://img.youtube.com/vi/9oif2M7rryQ/0.jpg)](https://youtu.be/9oif2M7rryQ)
</details>

## Installation

Download the latest release [here](https://github.com/SebastienDuruz/Window-Switcher/releases)

## Build Windows installer (scripted)

Prerequisite: install NSIS (so `makensis.exe` is available).

From the repo root:

`pwsh ./scripts/build-installer.ps1 -Version 0.4.0`

(Works in Windows PowerShell too: `powershell ./scripts/build-installer.ps1 -Version 0.4.0`.)

### Linux specific dependancies
Make sure your system is able to use the next commands:
- [wmctrl](https://linux.die.net/man/1/wmctrl)
- [export](https://linuxcommand.org/lc3_man_pages/exporth.html)

## Usage
1. Launch Window Switcher.
2. Configure which windows to preview:
3. Prefix filter: Only show windows with specific names.
4. Blacklist filter: Exclude unwanted windows.
5. Adjust the preview size and position.
6. Enjoy !

## License
This project is licensed under the [GPL3 License](LICENSE).

## Donations

If you find **Window Switcher** useful and would like to support its development, consider [buying me a coffee](https://buymeacoffee.com/sebastienduruz) ☕

Your support helps keep this project alive and motivates further improvements. Thank you! 🙌
