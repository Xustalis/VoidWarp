; VoidWarp Windows Installer - Inno Setup Script
; 用法：先运行项目根目录的 publish_windows.bat 生成 publish\VoidWarp-Windows，再运行：
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" platforms\windows\installer\VoidWarp.iss

#define MyAppName "VoidWarp"
#define MyAppNameCn "虚空传送"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "VoidWarp"
#define MyAppURL "https://github.com/XenithCode/VoidWarp"
#define PublishDir "..\..\..\publish\VoidWarp-Windows"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName} ({#MyAppNameCn})
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\VoidWarp
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile={#PublishDir}\LICENSE
OutputDir={#PublishDir}\..\output
OutputBaseFilename=VoidWarp-Windows-x64-Setup
SetupIconFile=
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "firewall"; Description: "安装完成后以管理员身份配置防火墙（推荐，便于 Android 发现本机）"; GroupDescription: "其他:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\VoidWarp.Windows.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\voidwarp_core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\VoidWarp.Windows.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\setup_firewall.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme
Source: "{#PublishDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\VoidWarp.Windows.exe"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\VoidWarp.Windows.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\setup_firewall.bat"; Description: "立即配置防火墙（推荐）"; Flags: runhidden waituntilterminated; Tasks: firewall

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not DirExists(ExpandConstant('{#PublishDir}')) then
  begin
    MsgBox('未找到发布文件。请先运行项目根目录的 publish_windows.bat 生成 publish\VoidWarp-Windows 后再编译安装包。', mbError, MB_OK);
    Result := False;
  end;
end;
