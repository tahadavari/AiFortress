#ifndef ReleaseVersion
  #define ReleaseVersion "1.1.0"
#endif
#ifndef ReleaseVersionNumber
  #define ReleaseVersionNumber "1.1.0"
#endif
#ifndef AppBinary
  #error AppBinary must be the path to the published AiFortress.exe
#endif

[Setup]
AppId={{5E7A6B94-31FA-4EC1-B6A4-7F7891674C0D}
AppName=AiFortress
AppVersion={#ReleaseVersion}
VersionInfoVersion={#ReleaseVersionNumber}
VersionInfoTextVersion={#ReleaseVersion}
AppPublisher=AiFortress
AppPublisherURL=https://github.com/tahadavari/AiFortress
AppSupportURL=https://github.com/tahadavari/AiFortress/issues
DefaultDirName={autopf}\AiFortress
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
WizardStyle=modern
SetupIconFile=..\src\AiFortress\Assets\AiFortress.ico
UninstallDisplayIcon={app}\AiFortress.exe
OutputBaseFilename=AiFortress-{#ReleaseVersion}-setup-win-x64
Compression=lzma2/fast
SolidCompression=no

[Tasks]
Name: "startatlogon"; Description: "Start AiFortress when I sign in"; GroupDescription: "Startup"

[Files]
Source: "installer-tasks.ps1"; Flags: dontcopy
Source: "{#AppBinary}"; DestDir: "{app}"; DestName: "AiFortress.exe"; Flags: ignoreversion
Source: "installer-tasks.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{commonprograms}\AiFortress"; Filename: "{app}\AiFortress.exe"

[Run]
Filename: "{app}\AiFortress.exe"; Description: "Open AiFortress"; Flags: postinstall nowait skipifsilent runascurrentuser

[Code]
function RunHelper(ScriptPath, Action, Extra: String): String;
var
  PowerShell, Args: String;
  ResultCode: Integer;
begin
  PowerShell := ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe');
  Args := '-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' +
    ScriptPath + '" -Action ' + Action + ' -InstallDirectory "' + ExpandConstant('{app}') + '"' + Extra;
  if not ExecAndLogOutput(PowerShell, Args, '', SW_HIDE, ewWaitUntilTerminated, ResultCode, nil) then
    Result := 'Could not start the AiFortress setup helper. Windows error ' + IntToStr(ResultCode) + '.'
  else if ResultCode <> 0 then
    Result := 'AiFortress could not ' + Action + ' (error ' + IntToStr(ResultCode) +
      '). Close any running copy of AiFortress and try again.'
  else
    Result := '';
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  ExtractTemporaryFile('installer-tasks.ps1');
  Result := RunHelper(ExpandConstant('{tmp}\installer-tasks.ps1'), 'prepare', '');
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  Error, Extra: String;
begin
  if CurStep = ssPostInstall then
  begin
    Extra := '';
    if WizardIsTaskSelected('startatlogon') then
      Extra := ' -StartAtLogon';
    Error := RunHelper(ExpandConstant('{app}\installer-tasks.ps1'), 'register', Extra);
    if Error <> '' then
      RaiseException(Error);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Error: String;
begin
  if CurUninstallStep = usUninstall then
  begin
    Error := RunHelper(ExpandConstant('{app}\installer-tasks.ps1'), 'cleanup', '');
    if Error <> '' then
      RaiseException(Error);
  end;
end;
