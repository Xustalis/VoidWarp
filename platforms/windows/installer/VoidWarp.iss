; VoidWarp Windows Installer - Self-Contained Edition
; Requires Inno Setup 6+
; 
; Build Instructions:
; 1. Generate Publish Artifacts: Run publish_windows.bat from project root
; 2. Ensure assets exist: platforms\windows\Assets\app.ico
; 3. Compile this script with ISCC

#define MyAppName "VoidWarp"
#define MyAppVersion "1.1.0"
#define MyAppPublisher "Xenith"
#define MyAppURL "https://github.com/XenithCode/VoidWarp"
#define MyAppExeName "VoidWarp.Windows.exe"
#define MyPublishDir "..\..\..\publish\VoidWarp-Windows"
#define MyAssetsDir "..\Assets"

[Setup]
; NOTE: The value of AppId uniquely identifies this application.
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Smart Installation Path: Default to Local AppData if non-admin, or Program Files if admin
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes

; Icon Configuration
SetupIconFile={#MyAssetsDir}\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
WindowVisible=yes

; Modern UI and Styling
WizardStyle=modern
WizardImageFile=
WizardSmallImageFile=

; Compression & Output
OutputDir={#MyPublishDir}\..\output
OutputBaseFilename=VoidWarp-Windows-x64-v{#MyAppVersion}-Setup
Compression=lzma2/ultra64
SolidCompression=yes

; Permissions & Architectures
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; License
LicenseFile={#MyPublishDir}\LICENSE

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Self-Contained Package: All files including .NET Runtime
Source: "{#MyPublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Firewall Setup (Silent)
Filename: "{app}\setup_firewall.bat"; Parameters: "silent"; Description: "配置防火墙规则"; Flags: runhidden waituntilterminated
; Launch App
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\VoidWarp"

[Messages]
WelcomeLabel2=本安装程序将在您的计算机上安装 [name]。%n%n此版本为自包含版本，无需安装 .NET Runtime 或其他依赖。%n%n点击"下一步"继续安装。

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
end;
