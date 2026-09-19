#define MyAppName "Clinic Suite"
#define MyAppVersion "1.0.0"
#define MyAppExeName "ClinicManagement.exe"

[Setup]
AppId={{7E0B9D67-9F7E-4D0A-8D2B-1C11C0000001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=AVN TechSphere
DefaultDirName={autopf}\Clinic Suite
DefaultGroupName=Clinic Suite
OutputDir=Output
OutputBaseFilename=ClinicSuiteSetup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64
PrivilegesRequired=admin
Uninstallable=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Files]
Source: "Payload\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{code:GetDataDir}"; Permissions: users-modify

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Icons]
Name: "{group}\Clinic Suite"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{commondesktop}\Clinic Suite"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\ImageMagickSetup.exe"; Parameters: "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR=""{app}\ImageMagick"""; StatusMsg: "Installing the bundled ImageMagick document converter..."; Flags: waituntilterminated runhidden; AfterInstall: VerifyImageMagick
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Clinic Suite"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent

[Code]
var
  DataPage: TInputDirWizardPage;
  DeveloperInfo: TNewStaticText;
function GetDataDir(Param: String): String;
begin
  Result := DataPage.Values[0];
end;

function ExistingDataDir(): String;
var
  ConfigFile, Marker, Value: String;
  Contents: AnsiString;
  StartPos, EndPos: Integer;
begin
  Result := '';
  ConfigFile := ExpandConstant('{commonappdata}\ClinicManagement\config.json');
  if not FileExists(ConfigFile) then
    exit;
  if not LoadStringFromFile(ConfigFile, Contents) then
    exit;

  Marker := '"DataPath":"';
  StartPos := Pos(Marker, Contents);
  if StartPos = 0 then
    exit;
  StartPos := StartPos + Length(Marker);
  EndPos := StartPos;
  while (EndPos <= Length(Contents)) and (Contents[EndPos] <> '"') do
    Inc(EndPos);
  if EndPos <= Length(Contents) then
  begin
    Value := Copy(Contents, StartPos, EndPos - StartPos);
    StringChangeEx(Value, '\\', '\', True);
    Result := Value;
  end;
end;

function PathsOverlap(FirstPath, SecondPath: String): Boolean;
var
  FirstWithSlash, SecondWithSlash: String;
begin
  FirstWithSlash := AddBackslash(FirstPath);
  SecondWithSlash := AddBackslash(SecondPath);
  Result :=
    (CompareText(FirstWithSlash, SecondWithSlash) = 0) or
    (CompareText(Copy(SecondWithSlash, 1, Length(FirstWithSlash)), FirstWithSlash) = 0) or
    (CompareText(Copy(FirstWithSlash, 1, Length(SecondWithSlash)), SecondWithSlash) = 0);
end;

procedure InitializeWizard;
var
  PreviousDataDir: String;
begin
  DeveloperInfo := TNewStaticText.Create(WizardForm);
  DeveloperInfo.Parent := WizardForm.WelcomePage;
  DeveloperInfo.Caption :=
    'Developed by AVN Techsphere' + #13#10 +
    'Contact: akhilvn@hotmail.com' + #13#10 +
    'For additional features or customization requests, please contact the developer.';
  DeveloperInfo.Font.Size := 8;
  DeveloperInfo.Font.Color := clGray;
  DeveloperInfo.AutoSize := False;
  DeveloperInfo.Alignment := taLeftJustify;
  DeveloperInfo.WordWrap := True;
  DeveloperInfo.Left := ScaleX(20);
  DeveloperInfo.Top := WizardForm.WelcomePage.ClientHeight - ScaleY(70);
  DeveloperInfo.Width := WizardForm.WelcomePage.ClientWidth - ScaleX(40);
  DeveloperInfo.Height := ScaleY(60);
  DeveloperInfo.Anchors := [akLeft, akBottom];

  DataPage := CreateInputDirPage(wpSelectDir, 'Clinic data location', 'Choose where patient data, database, documents and backups will be stored.', 'This location is preserved during upgrades and is not removed by uninstall.', False, 'ClinicManagementData');
  DataPage.Add('Data folder:');
  PreviousDataDir := ExistingDataDir();
  if PreviousDataDir <> '' then
    DataPage.Values[0] := PreviousDataDir
  else
    DataPage.Values[0] := ExpandConstant('{commonappdata}\ClinicManagement\Data');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  AppDir, DataDir: String;
begin
  Result := True;
  if CurPageID <> DataPage.ID then
    exit;

  DataDir := Trim(DataPage.Values[0]);
  AppDir := ExpandConstant('{app}');
  if DataDir = '' then
  begin
    MsgBox('Choose a clinic data folder before continuing.', mbError, MB_OK);
    Result := False;
    exit;
  end;
  if PathsOverlap(AppDir, DataDir) then
  begin
    MsgBox('The clinic data folder must be separate from the application installation folder.', mbError, MB_OK);
    Result := False;
    exit;
  end;
  if not ForceDirectories(DataDir) then
  begin
    MsgBox('The selected clinic data folder could not be created. Check the path and permissions.', mbError, MB_OK);
    Result := False;
  end;
end;

procedure VerifyImageMagick;
var
  InstallerPath, ExecutablePath, WorkingDir: String;
  ResultCode: Integer;
begin
  InstallerPath := ExpandConstant('{app}\ImageMagickSetup.exe');
  ExecutablePath := ExpandConstant('{app}\ImageMagick\magick.exe');
  WorkingDir := ExpandConstant('{app}\ImageMagick');
  if (not FileExists(ExecutablePath)) or
     (not Exec(ExecutablePath, '-version', WorkingDir, SW_HIDE, ewWaitUntilTerminated, ResultCode)) or
     (ResultCode <> 0) then
    MsgBox('Clinic Suite was installed without its bundled ImageMagick component. Image-to-PDF conversion will not be available. Please reinstall using a complete installer package.', mbError, MB_OK)
  else
    DeleteFile(InstallerPath);
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigDir, ConfigFile, DataDir, S: String;
begin
  if CurStep = ssPostInstall then
  begin
    ConfigDir := ExpandConstant('{commonappdata}\ClinicManagement');
    ForceDirectories(ConfigDir);
    ConfigFile := ConfigDir + '\config.json';
    DataDir := DataPage.Values[0];
    StringChangeEx(DataDir, '\', '\\', True);
    S := '{"DataPath":"' + DataDir + '"}';
    SaveStringToFile(ConfigFile, S, False);
  end;
end;
