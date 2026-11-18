; MinePix — Inno Setup installer definition.
; Build with:  ISCC.exe packaging\MinePix.iss
; Produces:    packaging\out\MinePixSetup-1.0.0.exe
;
; Install modes (user picks at install time):
;   * Current user     — %LocalAppData%\Programs\MinePix, no admin prompt
;   * All users        — Program Files, elevated
; Start Menu entry always created. Autostart is on by default; desktop shortcut is opt-in.

#define AppName        "MinePix"
#define AppVersion     "1.0.0"
#define AppPublisher   "Ayush Mondal"
#define AppURL         "https://github.com/A-y-u-s-h-9/MinePix"
#define AppExeName     "MinePix.exe"
#define AppMutex       "MinePix-SingleInstance"

[Setup]
AppId={{8B5D6E1A-3C47-4F21-9E20-57B3D42A1B9C}}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}/releases
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
AllowNoIcons=yes
LicenseFile=..\LICENSE
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline
OutputDir=out
OutputBaseFilename=MinePixSetup-{#AppVersion}
SetupIconFile=..\Assets\minepix.ico
UninstallDisplayIcon={app}\Assets\minepix.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
MinVersion=10.0
ChangesEnvironment=no
AppMutex={#AppMutex}
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
UninstallDisplayName={#AppName} {#AppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "autostart"; Description: "Launch MinePix automatically when I sign in"; GroupDescription: "At sign-in:"
Name: "desktopicon"; Description: "Create a Desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "..\bin\Release\net8.0-windows10.0.22621.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs; Excludes: "MinePix.pdb"
Source: "..\Assets\minepix.ico"; DestDir: "{app}\Assets"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Comment: "MinePix — a dynamic island for Windows"; IconFilename: "{app}\Assets\minepix.ico"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon; Comment: "MinePix — a dynamic island for Windows"; IconFilename: "{app}\Assets\minepix.ico"

[Registry]
; autostart — only the matching hive per install mode, removed on uninstall
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MinePix"; ValueData: """{app}\{#AppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue deletevalue; Check: not IsAdminInstallMode
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "MinePix"; ValueData: """{app}\{#AppExeName}"""; Tasks: autostart; Flags: uninsdeletevalue deletevalue; Check: IsAdminInstallMode

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName} now"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
