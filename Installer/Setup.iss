#define MyAppName "Clinic Management"
#define MyAppVersion "1.0.0"
#define MyAppExeName "ClinicManagement.exe"

[Setup]
AppId={{7E0B9D67-9F7E-4D0A-8D2B-1C11C0000001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\ClinicManagement
DefaultGroupName=Clinic Management
OutputDir=Output
OutputBaseFilename=ClinicManagementSetup
Compression=lzma
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

[Files]
Source: "..\bin\Release\net9.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{code:GetDataDir}"

[Icons]
Name: "{group}\Clinic Management"; Filename: "{app}\{#MyAppExeName}"
Name: "{commondesktop}\Clinic Management"; Filename: "{app}\{#MyAppExeName}"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch Clinic Management"; Flags: nowait postinstall skipifsilent

[Code]
var DataPage: TInputDirWizardPage;
function GetDataDir(Param: String): String;
begin Result := DataPage.Values[0]; end;
procedure InitializeWizard;
begin
  DataPage := CreateInputDirPage(wpSelectDir, 'Clinic data location', 'Choose where patient data, database, documents and backups will be stored.', 'You can change this later from the application settings.', False, 'ClinicManagementData');
  DataPage.Add('Data folder:');
  DataPage.Values[0] := 'C:\ClinicManagementData';
end;
procedure CurStepChanged(CurStep: TSetupStep);
var ConfigDir, ConfigFile, S: String;
begin
  if CurStep=ssPostInstall then begin
    ConfigDir := ExpandConstant('{commonappdata}\ClinicManagement');
    ForceDirectories(ConfigDir);
    ConfigFile := ConfigDir + '\config.json';
    S := '{"DataPath":"' + StringChangeEx(DataPage.Values[0], '\', '\\', True) + '"}';
    SaveStringToFile(ConfigFile, S, False);
  end;
end;
