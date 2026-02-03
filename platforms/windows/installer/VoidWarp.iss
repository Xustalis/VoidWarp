; VoidWarp Windows Installer - Advanced Inno Setup Script
; Requires Inno Setup 6+
; 
; Build Instructions:
; 1. Generate Publish Artifacts: Use publish_windows.bat
; 2. Ensure assets exist: platforms\windows\Assets\app.ico
; 3. Compile this script with ISCC

#define MyAppName "VoidWarp"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Xenith"
#define MyAppURL "https://github.com/XenithCode/VoidWarp"
#define MyAppExeName "VoidWarp.Windows.exe"
#define MyPublishDir "..\..\..\publish\VoidWarp-Windows"
#define MyAssetsDir "..\Assets"

[Setup]
; NOTE: The value of AppId uniquely identifies this application. Do not use the same AppId value in installers for other applications.
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}}
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
; Use the icon as the small wizard image if no BMP provided
WizardSmallImageFile=

; Compression & Output
OutputDir={#MyPublishDir}\..\output
OutputBaseFilename=VoidWarp-Windows-x64-Setup
Compression=lzma2/ultra64
SolidCompression=yes

; Permissions & Architectures
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64

; Digital Signature (Configure locally in IDE or CI)
; SignTool=signtool sign /a /n $qMy Certificate$q /t http://timestamp.digicert.com /fd sha256 $f

; License
LicenseFile={#MyPublishDir}\LICENSE

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main Executable
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
; Native DLLs and Dependencies
Source: "{#MyPublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyPublishDir}\*.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyPublishDir}\setup_firewall.bat"; DestDir: "{app}"; Flags: ignoreversion
; Docs
Source: "{#MyPublishDir}\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyPublishDir}\README.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme

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

[Code]
var
  DownloadPage: TDownloadWizardPage;
  vcRedistPath: string;

function InitializeSetup(): Boolean;
begin
  // Initialize Global Vars
  Result := True;
  vcRedistPath := ExpandConstant('{tmp}\vc_redist.x64.exe');
end;

function IsVCRedistInstalled: Boolean;
var
  regKey: string;
  installed: Cardinal;
begin
  // Check for Visual C++ 2015-2022 Redistributable (x64)
  // Registry key: HKLM\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64 -> Installed = 1
  regKey := 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64';
  if RegQueryDWordValue(HKLM, regKey, 'Installed', installed) then
  begin
    Result := (installed = 1);
  end
  else
  begin
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  // Add Download Page for VC++ Redistributable
  DownloadPage := CreateDownloadPage(SetupMessage(msgWizardPreparing), SetupMessage(msgPreparingDesc), nil);
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = wpReady) and (not IsVCRedistInstalled) then
  begin
    DownloadPage.Clear;
    DownloadPage.Add('https://aka.ms/vs/17/release/vc_redist.x64.exe', 'vc_redist.x64.exe', '');
    DownloadPage.Show;
    try
      try
        DownloadPage.Download; // Check errors
        Result := True;
      except
        SuppressibleMsgBox(AddPeriod(GetExceptionMessage), mbCriticalError, MB_OK, IDOK);
        Result := False;
      end;
    finally
      DownloadPage.Hide;
    end;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and (not IsVCRedistInstalled) then
  begin
    // Install VC++ Redistributable
    if FileExists(vcRedistPath) then
    begin
      WizardForm.StatusLabel.Caption := 'Installing Microsoft Visual C++ Redistributable...';
      WizardForm.ProgressGauge.Style := npbstMarquee;
      try
        if not Exec(vcRedistPath, '/install /quiet /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
        begin
          MsgBox('Visual C++ Redistributable installation failed. Code: ' + IntToStr(ResultCode), mbError, MB_OK);
        end;
      finally
        WizardForm.ProgressGauge.Style := npbstNormal;
      end;
    end;
  end;
end;
