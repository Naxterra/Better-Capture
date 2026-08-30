#define MyAppName "BetterCapture"
#ifndef MyAppVersion
#define MyAppVersion "1.0.3"
#endif
#define MyAppPublisher "Naxterra"
#define MyAppURL "https://github.com/Naxterra/Better-Capture"
#define MyAppExeName "BetterCapture.exe"
#define BuildOutput "..\artifacts\publish\win-x64"

[Setup]
AppId={{5F6769E5-7F06-4E51-A85C-F690B6D1E205}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
SetupArchitecture=x64
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\artifacts\installer
OutputBaseFilename=BetterCapture-v{#MyAppVersion}-Windows-x64-Setup
SetupIconFile=..\src\BetterCapture.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.26100
PrivilegesRequired=lowest
CloseApplications=force
RestartApplications=no
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=BetterCapture x64 installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "startmenuicon"; Description: "{cm:CreateStartMenuShortcut}"; GroupDescription: "{cm:ShortcutOptions}"; Flags: checkedonce
Name: "desktopicon"; Description: "{cm:CreateDesktopShortcut}"; GroupDescription: "{cm:ShortcutOptions}"; Flags: unchecked
Name: "startup"; Description: "{cm:StartWithWindows}"; GroupDescription: "{cm:StartupOptions}"; Flags: unchecked

[Files]
Source: "{#BuildOutput}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.appxrecipe,NaxCapture*,*\NaxCapture*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: startmenuicon
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "BetterCapture"; ValueData: """{app}\{#MyAppExeName}"" --background"; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[CustomMessages]
english.ShortcutOptions=Shortcut options:
english.CreateStartMenuShortcut=Create a Start menu shortcut
english.CreateDesktopShortcut=Create a desktop shortcut
english.StartupOptions=Windows startup:
english.StartWithWindows=Start BetterCapture with Windows
german.ShortcutOptions=Verknüpfungen:
german.CreateStartMenuShortcut=Verknüpfung im Startmenü erstellen
german.CreateDesktopShortcut=Desktopverknüpfung erstellen
german.StartupOptions=Windows-Autostart:
german.StartWithWindows=BetterCapture mit Windows starten

[Code]
procedure CurStepChanged(CurStep: TSetupStep);
begin
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('startup')) then
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'BetterCapture');
end;
