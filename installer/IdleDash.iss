; Installer voor IdleDash (Inno Setup). Wordt op GitHub vanzelf gebouwd: zie .github/workflows/release.yml.
; Installeert zonder beheerdersrechten in %LocalAppData%\Programs\IdleDash.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#define AppRepo "https://github.com/MeesjeKeesie/IdleDash"

[Setup]
; Vaste ID, nooit veranderen: zo herkent Windows een nieuwe versie als update
AppId={{6F1C3D52-8A47-4E2B-9C1F-3B7A5E0D9C21}
AppName=IdleDash
AppVersion={#AppVersion}
AppVerName=IdleDash {#AppVersion}
AppPublisher=MeesjeKeesie
AppPublisherURL={#AppRepo}
AppSupportURL={#AppRepo}/issues
AppUpdatesURL={#AppRepo}/releases
DefaultDirName={localappdata}\Programs\IdleDash
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=IdleDash-Setup-{#AppVersion}
SetupIconFile=..\Assets\IdleDash.ico
UninstallDisplayIcon={app}\IdleDash.exe
UninstallDisplayName=IdleDash
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Draait IdleDash nog? Dan vraagt de installer om hem eerst af te sluiten (via het icoon bij de klok)
AppMutex=IdleDash.SingleInstance
CloseApplications=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

[Languages]
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"

[Tasks]
Name: "autostart"; Description: "IdleDash starten als je inlogt op Windows"
Name: "desktopicon"; Description: "Snelkoppeling op het bureaublad"; Flags: unchecked

[Files]
Source: "..\publish\IdleDash.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\IdleDash"; Filename: "{app}\IdleDash.exe"
Name: "{autodesktop}\IdleDash"; Filename: "{app}\IdleDash.exe"; Tasks: desktopicon

[Registry]
; Autostart: dezelfde plek als de schakelaar "Starten met Windows" in IdleDash zelf
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "IdleDash"; ValueData: """{app}\IdleDash.exe"""; Tasks: autostart
; Bij verwijderen de autostart altijd opruimen, ook als je hem later in IdleDash hebt aangezet
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "IdleDash"; Flags: uninsdeletevalue

[Run]
Filename: "{app}\IdleDash.exe"; Description: "IdleDash nu starten"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Bij verwijderen je Google-login wissen (je instellingen en indeling blijven bewaard)
Type: filesandordirs; Name: "{userappdata}\IdleDash\google-login"
