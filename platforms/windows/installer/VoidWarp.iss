; VoidWarp Windows Installer - Inno Setup Script
; 用法：先运行项目根目录的 publish_windows.bat 生成 publish\VoidWarp-Windows
; 然后运行本目录的 build_installer.bat 或使用 ISCC 编译此脚本

#define VwName "VoidWarp"
#define VwVersion "2.0.0.0"
#define VwPublisher "Xenith"
#define VwURL "https://github.com/XenithCode/VoidWarp"
#define VwPublishDir "..\..\..\publish\VoidWarp-Windows"
#define VwExeName "VoidWarp.Windows.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}}
AppName={#VwName}
AppVersion={#VwVersion}
AppPublisher={#VwPublisher}
AppPublisherURL={#VwURL}
DefaultDirName={autopf}\{#VwName}
DefaultGroupName={#VwName}
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=yes
LicenseFile={#VwPublishDir}\LICENSE
OutputDir={#VwPublishDir}\..\output
OutputBaseFilename=VoidWarp-Windows-x64-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#VwPublishDir}\{#VwExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#VwPublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#VwPublishDir}\*.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#VwPublishDir}\setup_firewall.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#VwPublishDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#VwPublishDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

[Icons]
Name: "{group}\{#VwName}"; Filename: "{app}\{#VwExeName}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#VwName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#VwName}"; Filename: "{app}\{#VwExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; 安装后自动配置防火墙（静默执行，无需用户操作）
Filename: "{app}\setup_firewall.bat"; Parameters: "silent"; Description: "配置防火墙以允许设备发现..."; Flags: runhidden waituntilterminated
Filename: "{app}\{#VwExeName}"; Description: "{cm:LaunchProgram,{#VwName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\VoidWarp"
