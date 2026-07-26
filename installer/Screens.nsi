; Screens installer (NSIS). Per-user install, no UAC prompt.
; Build: makensis /DVERSION=1.0.0 Screens.nsi
; See ../docs/RELEASE.md for the full release sequence.

!include "MUI2.nsh"

!ifndef VERSION
  !define VERSION "1.0.0"
!endif

Name "Screens"
OutFile "Screens-Setup-${VERSION}.exe"
Unicode true

; Per-user install directory — never needs admin rights, so no UAC prompt.
InstallDir "$LOCALAPPDATA\Programs\Screens"
InstallDirRegKey HKCU "Software\Screens" "InstallDir"
RequestExecutionLevel user

!define MUI_ABORTWARNING
!define MUI_ICON "..\src\Screens.App\Assets\Icons\app.ico"
!define MUI_UNICON "..\src\Screens.App\Assets\Icons\app.ico"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"

Section "Screens (required)" SEC_MAIN
  SectionIn RO
  SetOutPath "$INSTDIR"

  ; Overwrite an existing install in place — silent /S is fully non-interactive.
  SetOverwrite on
  File /r "..\src\Screens.App\bin\Release\net8.0\win-x64\publish\*.*"

  WriteRegStr HKCU "Software\Screens" "Version" "${VERSION}"
  WriteRegStr HKCU "Software\Screens" "InstallDir" "$INSTDIR"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\Screens"
  CreateShortcut "$SMPROGRAMS\Screens\Screens.lnk" "$INSTDIR\Screens.exe"
  CreateShortcut "$SMPROGRAMS\Screens\Uninstall Screens.lnk" "$INSTDIR\Uninstall.exe"

  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Screens" \
    "DisplayName" "Screens"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Screens" \
    "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Screens" \
    "InstallLocation" "$\"$INSTDIR$\""
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Screens" \
    "DisplayIcon" "$\"$INSTDIR\Screens.exe$\""
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Screens" \
    "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Screens" \
    "Publisher" "Screens"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Screens" \
    "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Screens" \
    "NoRepair" 1
SectionEnd

Section /o "Desktop shortcut" SEC_DESKTOP
  CreateShortcut "$DESKTOP\Screens.lnk" "$INSTDIR\Screens.exe"
SectionEnd

Section "Uninstall"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir /r "$INSTDIR"

  Delete "$SMPROGRAMS\Screens\Screens.lnk"
  Delete "$SMPROGRAMS\Screens\Uninstall Screens.lnk"
  RMDir "$SMPROGRAMS\Screens"
  Delete "$DESKTOP\Screens.lnk"

  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\Screens"
  DeleteRegKey HKCU "Software\Screens"

  ; %APPDATA%\Screens (user templates, settings, session) is intentionally
  ; preserved so re-installing or upgrading never loses user data.
SectionEnd
