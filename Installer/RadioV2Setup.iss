; RadioV2 Inno Setup Script
; Requires Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
;
; Build steps:
;   1. dotnet publish -c Release -r win-x64 --self-contained false -o ..\publish
;   2. Open this file in Inno Setup Compiler and click Build → Compile
;   3. Output: Installer\Output\RadioV2Setup.exe
;
; The installer targets %LocalAppData%\RadioV2 by default.
; No administrator rights required (PrivilegesRequired=lowest).

#define MyAppName    "RadioV2"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Natboa"
#define MyAppURL     "https://github.com/Natboa/radioV2"
#define MyAppExeName "RadioV2.exe"
#define PublishDir   "..\publish"
#define DataDir      "..\Data"

[Setup]
AppId={{8D3F1A2B-4C5E-4D6F-9A0B-1C2D3E4F5A6B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=Output
OutputBaseFilename=RadioV2Setup
SetupIconFile=..\Assets\RadioV2_Logo.ico
Compression=lzma2/ultra64
SolidCompression=yes
; No admin rights needed — installs entirely in %LocalAppData%
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
; Windows 10 1809 minimum (required for Mica/FluentWindow)
MinVersion=10.0.17763
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
; All published app files (RadioV2.exe, WPF-UI assets, native VLC DLLs, etc.)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

; Station database — overwritten on every install/update
Source: "{#DataDir}\stations.db"; DestDir: "{app}\Data"; Flags: ignoreversion

; NOTE: userdata.db is NOT listed here — the app creates it on first run.
;       Inno Setup only touches files it explicitly lists, so the user's
;       favourites and settings are always preserved on update.

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the entire install folder on uninstall (including userdata.db)
Type: filesandordirs; Name: "{app}"

[Code]
// ── .NET 8 Desktop Runtime prerequisite check ─────────────────────────────

function IsDotNet8Installed(): Boolean;
var
  KeyPath: String;
  Version: String;
begin
  Result := False;
  KeyPath := 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App';
  if RegQueryStringValue(HKLM, KeyPath, '8.0.0', Version) then
    Result := True
  else if RegQueryStringValue(HKCU, KeyPath, '8.0.0', Version) then
    Result := True
  else
  begin
    // Also check for any 8.x version by looking at the presence of the key
    if RegKeyExists(HKLM, KeyPath) then
      Result := True;
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet8Installed() then
  begin
    if MsgBox(
      '.NET 8 Desktop Runtime is required but was not found on this machine.' + #13#10 + #13#10 +
      'Click OK to open the Microsoft download page where you can download and install it, ' +
      'then run this installer again.' + #13#10 + #13#10 +
      'Click Cancel to abort the installation.',
      mbConfirmation, MB_OKCANCEL) = IDOK then
    begin
      ShellExec('open',
        'https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.0-windows-x64-installer',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end;
    Result := False;
  end;
end;

// ── Custom install path warning ───────────────────────────────────────────

function NextButtonClick(CurPageID: Integer): Boolean;
var
  InstallDir: String;
  LocalAppData: String;
begin
  Result := True;
  if CurPageID = wpSelectDir then
  begin
    InstallDir := WizardDirValue();
    LocalAppData := ExpandConstant('{localappdata}');
    // Warn if the user chose a path outside %LocalAppData%
    if Pos(LowerCase(LocalAppData), LowerCase(InstallDir)) = 0 then
    begin
      MsgBox(
        'You chose a custom install location outside %LocalAppData%.' + #13#10 + #13#10 +
        'Make sure you have write permissions to this folder, as RadioV2 stores its ' +
        'database and user data there.',
        mbInformation, MB_OK);
    end;
  end;
end;
