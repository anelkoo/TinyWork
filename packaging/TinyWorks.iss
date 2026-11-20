; Windows installer. build.ps1 runs: ISCC /DVersion=<ver> packaging\TinyWorks.iss
; Installs per user (no admin prompt), adds a Start menu entry and an uninstaller. Saves stay in %APPDATA%\TinyWorks.
#ifndef Version
  #define Version "0.0.0"
#endif

[Setup]
AppId={{6E0F5C1B-7A3D-4C52-9B8E-2D4A1F7C9E31}
AppName=Tiny Works
AppVersion={#Version}
AppPublisher=anelkoo
AppPublisherURL=https://github.com/anelkoo/TinyWork
DefaultDirName={autopf}\Tiny Works
DefaultGroupName=Tiny Works
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\export
OutputBaseFilename=TinyWorks-{#Version}-windows-setup
SetupIconFile=icon.ico
UninstallDisplayIcon={app}\icon.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\export\windows\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion
Source: "icon.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Tiny Works"; Filename: "{app}\TinyWorks.exe"; IconFilename: "{app}\icon.ico"
Name: "{autodesktop}\Tiny Works"; Filename: "{app}\TinyWorks.exe"; IconFilename: "{app}\icon.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\TinyWorks.exe"; Description: "{cm:LaunchProgram,Tiny Works}"; Flags: nowait postinstall skipifsilent
