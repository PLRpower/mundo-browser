; Script Inno Setup pour MundoBrowser
; Généré automatiquement par Gemini CLI

[Setup]
; AppId est l'identifiant unique de l'application
AppId={{9F7A2A6B-8B31-4B9F-AF04-567F4F79F8C2}
AppName=MundoBrowser
AppVersion=1.0
AppPublisher=Mundo
AppPublisherURL=https://github.com/votre-repo/mundo-browser
AppSupportURL=https://github.com/votre-repo/mundo-browser/issues
AppUpdatesURL=https://github.com/votre-repo/mundo-browser/releases
DefaultDirName={autopf}\MundoBrowser
DefaultGroupName=MundoBrowser
AllowNoIcons=yes
; Icone de l'installateur
SetupIconFile=MundoBrowser\Assets\Icons\logo.ico
OutputDir=.\InstallerOutput
OutputBaseFilename=MundoBrowser_Setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Fichiers de l'application (basé sur le résultat du build dotnet publish)
Source: "MundoBrowser\bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\MundoBrowser"; Filename: "{app}\MundoBrowser.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\MundoBrowser"; Filename: "{app}\MundoBrowser.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\MundoBrowser.exe"; Description: "{cm:LaunchProgram,MundoBrowser}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Registry]
; Enregistrement pour l'association de protocoles (Navigateur par défaut)
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\MundoBrowser"; ValueType: string; ValueName: ""; ValueData: "Mundo Browser"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\MundoBrowser\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\MundoBrowser.exe,0"
Root: HKLM; Subkey: "Software\Clients\StartMenuInternet\MundoBrowser\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MundoBrowser.exe"""

; Capacités du navigateur (Ce qui permet à Windows de le lister dans les réglages)
Root: HKLM; Subkey: "Software\MundoBrowser\Capabilities"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Un navigateur web moderne et rapide."; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\MundoBrowser\Capabilities"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: "{app}\MundoBrowser.exe,0"
Root: HKLM; Subkey: "Software\MundoBrowser\Capabilities"; ValueType: string; ValueName: "ApplicationName"; ValueData: "Mundo Browser"
Root: HKLM; Subkey: "Software\MundoBrowser\Capabilities\FileAssociations"; ValueType: string; ValueName: ".htm"; ValueData: "MundoBrowserHTML"
Root: HKLM; Subkey: "Software\MundoBrowser\Capabilities\FileAssociations"; ValueType: string; ValueName: ".html"; ValueData: "MundoBrowserHTML"
Root: HKLM; Subkey: "Software\MundoBrowser\Capabilities\FileAssociations"; ValueType: string; ValueName: ".pdf"; ValueData: "MundoBrowserHTML"
Root: HKLM; Subkey: "Software\MundoBrowser\Capabilities\URLAssociations"; ValueType: string; ValueName: "http"; ValueData: "MundoBrowserHTML"
Root: HKLM; Subkey: "Software\MundoBrowser\Capabilities\URLAssociations"; ValueType: string; ValueName: "https"; ValueData: "MundoBrowserHTML"

; Enregistrement de l'application
Root: HKLM; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "Mundo Browser"; ValueData: "Software\MundoBrowser\Capabilities"

; Définition du type de fichier MundoBrowserHTML
Root: HKCR; Subkey: "MundoBrowserHTML"; ValueType: string; ValueName: ""; ValueData: "Mundo Browser HTML Document"; Flags: uninsdeletekey
Root: HKCR; Subkey: "MundoBrowserHTML\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\MundoBrowser.exe,0"
Root: HKCR; Subkey: "MundoBrowserHTML\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\MundoBrowser.exe"" ""%1"""

