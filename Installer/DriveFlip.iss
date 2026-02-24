; DriveFlip Inno Setup Script
; Requires Inno Setup 6.3+ (https://jrsoftware.org/isinfo.php)
;
; Build: Run Installer\Build-Installer.ps1 which publishes the app
;        and invokes this script automatically.
;
; Exit codes returned to Windows Store (all unique):
;   0 = Installation successful
;   1 = Installation already in progress (SetupMutex / AppMutex)
;   2 = Installation cancelled by user
;   4 = Disk space is full / fatal install error
;   7 = Application already exists (same version detected)

#define MyAppName      "DriveFlip"
#define MyAppVersion   "2.0.0"
#define MyAppPublisher "Vitendy"
#define MyAppURL       "https://vitendy.com"
#define MyAppExeName   "DriveFlip.exe"

[Setup]
AppId={{B7A3F1E0-4D2C-4A8B-9E6F-1C3D5A7B9E0F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
OutputDir=..\Installer\Output
OutputBaseFilename=DriveFlip_Setup_{#MyAppVersion}
SetupIconFile=..\Assets\logo-icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Single-instance: block if DriveFlip or setup is already running
AppMutex=Global\DriveFlip_B7A3F1E0
SetupMutex=DriveFlipSetup_B7A3F1E0
MinVersion=10.0
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; All published output files
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[Code]
// Detect if the exact same version is already installed.
// Returns non-empty string to abort → Inno Setup exit code 7.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  InstalledVersion: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM,
    'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#SetupSetting("AppId")}_is1',
    'DisplayVersion', InstalledVersion) then
  begin
    if InstalledVersion = '{#MyAppVersion}' then
      Result := '{#MyAppName} ' + InstalledVersion + ' is already installed.';
  end;
end;

// --- Future: License activation page ---
// Uncomment and expand when ready to integrate license activation into setup.
//
// var LicensePage: TInputQueryWizardPage;
//
// procedure InitializeWizard();
// begin
//   LicensePage := CreateInputQueryPage(
//     wpSelectDir,
//     'License Activation',
//     'Enter your license key to activate DriveFlip.',
//     'You can also activate later from Settings inside the app.');
//   LicensePage.Add('License Key:', False);
// end;
//
// function NextButtonClick(CurPageID: Integer): Boolean;
// var
//   Key: String;
// begin
//   Result := True;
//   if CurPageID = LicensePage.ID then
//   begin
//     Key := LicensePage.Values[0];
//     if Key <> '' then
//     begin
//       // TODO: Call activation API or write key to AppData for app to pick up
//       SaveStringToFile(
//         ExpandConstant('{localappdata}\DriveFlip\pending_license.txt'),
//         Key, False);
//     end;
//   end;
// end;
