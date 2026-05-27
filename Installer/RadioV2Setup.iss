; RadioV2 Inno Setup Script
; Requires Inno Setup 6.x  (https://jrsoftware.org/isinfo.php)
;
; Build steps:
;   1. dotnet publish -c Release -r win-x64 --self-contained true -o ..\publish
;   2. Open this file in Inno Setup Compiler and click Build → Compile
;   3. Output: Installer\Output\RadioV2Setup.exe
;
; The installer targets %LocalAppData%\RadioV2 by default.
; No administrator rights required (PrivilegesRequired=lowest).

#define MyAppName    "RadioV2"
#define MyAppVersion "1.0.5"
#define MyAppPublisher "Natboa"
#define MyAppURL     "https://github.com/Natboa/RadioV2"
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
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the entire install folder on uninstall (including userdata.db)
Type: filesandordirs; Name: "{app}"

[Code]
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Kill any running instance of RadioV2 before files are copied,
  // otherwise locked DLLs (clrjit.dll etc.) cause "Access denied" errors.
  Exec('taskkill.exe', '/f /im RadioV2.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := True;
end;

