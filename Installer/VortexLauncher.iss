; Build the application first with publish-win-x64.ps1.
; Then open this file with Inno Setup Compiler.

#define AppName "Vortex Launcher"
#ifndef AppVersion
#define AppVersion "0.1.4"
#endif
#define AppPublisher "Vortex Launcher"
#define AppExeName "Vortex Launcher.exe"
#define PublishDir "..\\publish\\win-x64"

[Setup]
AppId={{D1F0D39B-9C6D-4EB0-BD39-7D37A7C2F4F8}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={localappdata}\\Programs\\Vortex Launcher
DefaultGroupName={#AppName}
OutputDir=..\\release
OutputBaseFilename=VortexLauncherSetup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesInstallIn64BitMode=x64
UninstallDisplayIcon={app}\\{#AppExeName}

[Files]
; The self-contained single-file executable includes its runtime and native libraries.
; Never bundle portable users' config, accounts, instances or downloaded runtimes.
Source: "{#PublishDir}\\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\\{#AppName}"; Filename: "{app}\\{#AppExeName}"
Name: "{autodesktop}\\{#AppName}"; Filename: "{app}\\{#AppExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
