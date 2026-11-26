[Setup]
AppId=Intranet.Tools.ImageCompress
AppName=压缩图片
AppVersion={#AppVersion}
AppPublisher=lzl32947
AppPublisherURL=https://github.com/lzl32947/Intranet.Tools.ImageCompress
AppUpdatesURL=https://github.com/lzl32947/Intranet.Tools.ImageCompress/releases
DefaultDirName={localappdata}\Programs\Intranet.Tools.ImageCompress
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
OutputBaseFilename=Intranet.Tools.ImageCompress-Setup-x64
SetupIconFile=Assets\ImagePress.ico
UninstallDisplayIcon={app}\ImagePress.exe
VersionInfoVersion={#PackageVersion}
VersionInfoProductName=Intranet.Tools.ImageCompress
SignTool=publisher
SignedUninstaller=yes
ChangesAssociations=yes
RestartApplications=no

[Languages]
Name: "zh"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Files]
Source: "{#Layout}\Menu\Menu.ps1"; DestDir: "{app}\Menu"; Flags: ignoreversion
Source: "{#Layout}\Menu\Publisher.cer"; DestDir: "{app}\Menu"; Flags: ignoreversion
Source: "{#Layout}\*"; Excludes: "Menu\Menu.ps1,Menu\Publisher.cer"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\压缩图片"; Filename: "{app}\ImagePress.exe"; WorkingDir: "{app}"

[Run]
Filename: "https://dotnet.microsoft.com/download/dotnet/10.0"; Description: "打开微软下载页，获取 .NET 10 桌面运行时 x64"; Flags: shellexec postinstall skipifsilent unchecked; Check: NeedsDesktopRuntime
Filename: "{app}\ImagePress.exe"; Description: "打开压缩图片"; Flags: nowait postinstall skipifsilent unchecked; Check: HasDesktopRuntime

[Code]
var
  MenuExitCode: Integer;

function HasDesktopRuntime: Boolean;
var
  Versions: TArrayOfString;
  Index: Integer;
  Found: TFindRec;
  SharedDir: String;
begin
  Result := False;
  if RegGetValueNames(HKLM32, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', Versions) then
    for Index := 0 to GetArrayLength(Versions) - 1 do
      if Pos('10.0.', Versions[Index]) = 1 then begin Result := True; Exit; end;
  SharedDir := ExpandConstant('{commonpf64}\dotnet\shared\Microsoft.WindowsDesktop.App\');
  if FindFirst(SharedDir + '10.0.*', Found) then begin
    try
      repeat
        Result := FileExists(SharedDir + Found.Name + '\PresentationFramework.dll');
      until Result or not FindNext(Found);
    finally
      FindClose(Found);
    end;
  end;
end;

function NeedsDesktopRuntime: Boolean;
begin
  Result := not HasDesktopRuntime;
end;

function PowerShell: String;
begin
  Result := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
end;

function MenuAction(const Action, ScriptPath, Certificate: String): Integer;
var
  Args: String;
  ErrorPath: String;
begin
  ErrorPath := ExpandConstant('{tmp}\imagepress-menu-error.txt');
  DeleteFile(ErrorPath);
  Args := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath +
    '" -Action ' + Action + ' -AppDirectory "' + ExpandConstant('{app}') + '" -Version {#PackageVersion}' +
    ' -ErrorLog "' + ErrorPath + '"';
  if Certificate <> '' then Args := Args + ' -Certificate "' + Certificate + '"';
  if not Exec(PowerShell, Args, '', SW_HIDE, ewWaitUntilTerminated, Result) then Result := -1;
  Log(Format('Explorer menu %s exit code %d', [Action, Result]));
end;

function MenuError: String;
var
  Lines: TArrayOfString;
  Index: Integer;
begin
  Result := '';
  if LoadStringsFromFile(ExpandConstant('{tmp}\imagepress-menu-error.txt'), Lines) then
    for Index := 0 to GetArrayLength(Lines) - 1 do Result := Result + #13#10 + Lines[Index];
  Log('Explorer menu error: ' + Result);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ScriptPath, Certificate: String;
  Code: Integer;
begin
  Result := '';
  ExtractTemporaryFile('Menu.ps1');
  ExtractTemporaryFile('Publisher.cer');
  ScriptPath := ExpandConstant('{tmp}\Menu.ps1');
  Certificate := ExpandConstant('{tmp}\Publisher.cer');
  Code := MenuAction('Prepare', ScriptPath, Certificate);
  if Code = 10 then begin
    if SuppressibleMsgBox('首次注册 Windows 11 第一层右键菜单需要管理员确认，将随附的发布者证书添加到本地计算机的「受信任人」。是否继续？',
      mbConfirmation, MB_YESNO, IDNO) <> IDYES then begin
      Result := '尚未授权注册右键菜单。'; Exit;
    end;
    { The elevated command contains only the fixed public certificate embedded at build time. }
    if not ShellExec('runas', PowerShell, '-NoLogo -NoProfile -NonInteractive -EncodedCommand {#TrustCommand}',
      '', SW_HIDE, ewWaitUntilTerminated, Code) or (Code <> 0) then begin
      Result := '未能信任菜单发布者证书。管理员可以确认后重试。'; Exit;
    end;
    Code := MenuAction('Prepare', ScriptPath, Certificate);
  end;
  if Code <> 0 then Result := '无法准备右键菜单。' + MenuError;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    MenuExitCode := MenuAction('Register', ExpandConstant('{app}\Menu\Menu.ps1'), '');
    if MenuExitCode <> 0 then
      SuppressibleMsgBox('桌面程序已安装，右键菜单注册失败。关闭正在使用压缩图片的窗口，再运行安装器可重试。' + MenuError, mbError, MB_OK, IDOK);
  end;
end;

procedure CurPageChanged(Page: Integer);
begin
  if (Page = wpFinished) and (MenuExitCode <> 0) then
    WizardForm.FinishedLabel.Caption := '桌面程序已安装。请重新运行安装器完成右键菜单注册。';
end;

function GetCustomSetupExitCode: Integer;
begin
  if MenuExitCode <> 0 then Result := 20 else Result := 0;
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
begin
  if Step = usUninstall then
    if MenuAction('Remove', ExpandConstant('{app}\Menu\Menu.ps1'), '') <> 0 then
      RaiseException('未能注销右键菜单。' + MenuError);
end;
