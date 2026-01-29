; VoidWarp Windows Installer - Optimized Inno Setup Script
#define MyAppName "VoidWarp"
#define MyAppNameCn "虚空传送"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Xenith"
#define MyAppURL "https://github.com/XenithCode/VoidWarp"
#define PublishDir "..\..\..\publish\VoidWarp-Windows"
#define MyAppExeName "VoidWarp.Windows.exe"

[Setup]
; 这里的 AppId 建议生成一个固定的，防止版本升级时安装两个程序
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName} ({#MyAppNameCn})
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; 自动检测并提醒用户关闭正在运行的程序
AppGreetingsExplicitlyAcknowledged=yes
CloseApplications=yes
RestartApplications=yes
; 其它设置
LicenseFile={#PublishDir}\LICENSE
OutputDir={#PublishDir}\..\output
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
Name: "firewall"; Description: "配置 Windows 防火墙允许 UDP 广播发现 (推荐)"; GroupDescription: "网络配置:"; Flags: checkedonce

[Files]
; 1. 核心程序
Source: "{#PublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; 2. 批量包含 DLL 和配置文件，避免漏掉第三方库
Source: "{#PublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\*.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\setup_firewall.bat"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
; 3. 包含所有 runtime 依赖子目录 (例如 runtimes\win-x64\...)
Source: "{#PublishDir}\runtimes\*"; DestDir: "{app}\runtimes"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; 修改为以管理员权限运行防火墙脚本
Filename: "{app}\setup_firewall.bat"; Description: "正在配置防火墙规则..."; Flags: runascurrentuser runhidden waituntilterminated; Tasks: firewall
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; 卸载时可选地清理防火墙规则（如果你的 bat 支持 /clean 参数的话）
; Filename: "{app}\setup_firewall.bat"; Parameters: "/clean"; Flags: runhidden

[UninstallDelete]
; 卸载时删除生成的日志或临时文件
Type: filesandordirs; Name: "{localappdata}\VoidWarp"