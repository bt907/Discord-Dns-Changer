; ─────────────────────────────────────────────────────────────────────────────
; DNS Switcher — Inno Setup installer script
; Produces a single self-contained setup exe (~155 MB).
; The installed app requires Windows 10/11 x64. No .NET install needed.
; ─────────────────────────────────────────────────────────────────────────────

#define AppName      "DNS Switcher"
#define AppVersion   "1.0.0"
#define AppPublisher "DNS Switcher"
#define AppExe       "DNS_Switcher.exe"
#define SourceDir    "..\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{F3A1B2C4-8E7D-4A9F-B3C2-1D5E6F7A8B9C}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
; Require admin for install (so it lands in Program Files properly)
PrivilegesRequired=admin
OutputDir=.
OutputBaseFilename=DNS_Switcher_Setup
Compression=lzma2/ultra64
SolidCompression=yes
; No separate .NET installer needed — everything is bundled in the exe
WizardStyle=modern
CloseApplications=yes
; Windows 10 minimum
MinVersion=10.0.17763

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon";     Description: "Create a &desktop shortcut";        GroupDescription: "Additional icons:"; Flags: unchecked
Name: "startmenuicon";   Description: "Create a &Start Menu shortcut";      GroupDescription: "Additional icons:"; Flags: checkedonce

[Files]
; The single self-contained executable (includes .NET 8 runtime)
Source: "{#SourceDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu shortcut — note: runas so UAC prompt fires on launch
Name: "{group}\{#AppName}";        Filename: "{app}\{#AppExe}"; Parameters: ""; \
  Comment: "Change DNS settings on Windows 10/11"
; Desktop shortcut (optional) — placed in common desktop so it works for all users
Name: "{commondesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; \
  Tasks: desktopicon; \
  Comment: "Change DNS settings on Windows 10/11"

[Run]
; Offer to launch the app after install finishes
Filename: "{app}\{#AppExe}"; \
  Description: "Launch {#AppName} now"; \
  Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallDelete]
; Clean up nothing extra — app writes no files outside its own folder
Type: filesandordirs; Name: "{app}"

[Code]
// ── Pre-install: check Windows version ──────────────────────────────────────
function InitializeSetup(): Boolean;
var
  Ver: TWindowsVersion;
begin
  GetWindowsVersionEx(Ver);
  // Require Windows 10 build 17763+ (October 2018 Update)
  if (Ver.Major < 10) or ((Ver.Major = 10) and (Ver.Build < 17763)) then
  begin
    MsgBox('DNS Switcher requires Windows 10 (version 1809) or later.' + #13#10 +
           'Please update Windows before installing.', mbError, MB_OK);
    Result := False;
    Exit;
  end;
  Result := True;
end;
