#define MyAppName "FuelPro"
#define MyAppVersion "1.0.1"
#define MyAppPublisher "VKD Petroleum"
#define MyAppExeName "FuelPro.UI.exe"

[Setup]
; App Metadata
AppId={{5B24A61B-3914-4CC7-B9C0-8D3A8E0FF6A2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\VKD Petroleum\{#MyAppName}
DefaultGroupName=VKD Petroleum
DisableProgramGroupPage=yes

; Installer Output
OutputDir=.\installer_output
OutputBaseFilename=FuelPro_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes

; Cosmetics
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; The publish folder must contain the built application
Source: ".\publish_output\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; Exclude PDB files from the final installer
Source: ".\publish_output\*.pdb"; DestDir: "{app}"; Flags: dontcopy

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent
