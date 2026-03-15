; Window Switcher - NSIS installer
; Build via: makensis /DAPP_VERSION=<version> /DPUBLISH_DIR=... /DPUBLISH_GLOB=... /DOUT_FILE=... scripts\assets\installer\WindowSwitcher.nsi

!include "MUI2.nsh"

!define APP_NAME "Window Switcher"
!define APP_PUBLISHER "Window Switcher"
!define APP_EXE "WindowSwitcher.exe"
!define APP_ICON_REL "Assets\\WS_logo.ico"

!ifndef APP_VERSION
!error "APP_VERSION is required. Pass /DAPP_VERSION=x.y.z"
!endif

!ifndef PUBLISH_DIR
!error "PUBLISH_DIR is required. Pass /DPUBLISH_DIR=path_to_dotnet_publish_output"
!endif

!ifndef PUBLISH_GLOB
!error "PUBLISH_GLOB is required. Pass /DPUBLISH_GLOB=path_to_dotnet_publish_output_with_wildcard"
!endif

!ifndef OUT_FILE
!define OUT_FILE "WindowSwitcher-setup.exe"
!endif

OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\\WindowSwitcher"
InstallDirRegKey HKCU "Software\\${APP_PUBLISHER}\\${APP_NAME}" "InstallDir"
RequestExecutionLevel user
Unicode True

Name "${APP_NAME} ${APP_VERSION}"

!define MUI_ABORTWARNING
!define MUI_ICON "../../../src/WindowSwitcher/Assets/WS_logo.ico"
!define MUI_UNICON "../../../src/WindowSwitcher/Assets/WS_logo.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Install" SecInstall
  SetOutPath "$INSTDIR"
  File /r "${PUBLISH_GLOB}"

  ; Registry: remember install dir
  WriteRegStr HKCU "Software\\${APP_PUBLISHER}\\${APP_NAME}" "InstallDir" "$INSTDIR"

  ; Shortcuts
  CreateDirectory "$SMPROGRAMS\\${APP_NAME}"
  CreateShortCut "$SMPROGRAMS\\${APP_NAME}\\${APP_NAME}.lnk" "$INSTDIR\\${APP_EXE}" "" "$INSTDIR\\${APP_ICON_REL}"
  CreateShortCut "$DESKTOP\\${APP_NAME}.lnk" "$INSTDIR\\${APP_EXE}" "" "$INSTDIR\\${APP_ICON_REL}"

  ; Uninstall registration
  WriteUninstaller "$INSTDIR\\Uninstall.exe"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "DisplayName" "${APP_NAME}"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "Publisher" "${APP_PUBLISHER}"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "DisplayVersion" "${APP_VERSION}"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "DisplayIcon" "$INSTDIR\\${APP_ICON_REL}"
  WriteRegStr HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "UninstallString" "$\"$INSTDIR\\Uninstall.exe$\""
  WriteRegDWORD HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "NoModify" 1
  WriteRegDWORD HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\\${APP_NAME}.lnk"
  Delete "$SMPROGRAMS\\${APP_NAME}\\${APP_NAME}.lnk"
  RMDir "$SMPROGRAMS\\${APP_NAME}"

  DeleteRegKey HKCU "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\${APP_NAME}"
  DeleteRegKey HKCU "Software\\${APP_PUBLISHER}\\${APP_NAME}"

  RMDir /r "$INSTDIR"
SectionEnd
