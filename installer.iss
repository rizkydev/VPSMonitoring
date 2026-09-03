; =====================================================
; Inno Setup Script untuk VPS Monitoring Desktop
; Generated untuk portofolio @rizkydev
; Universal — tidak spesifik ke satu provider
; =====================================================

#define MyAppName "VPS Monitoring Desktop"
#define MyAppDisplayName "VPS Monitoring Desktop"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Rizky (rizkydev)"
#define MyAppURL "https://github.com/rizkydev/VPSMonitoring"
#define MyAppExeName "VPS Monitor Desktop App.exe"
#define MyAppCopyright "Copyright (C) 2026 Rizky (rizkydev)"

[Setup]
; NOTE: AppId adalah GUID unik untuk aplikasi. JANGAN pakai GUID yang sama untuk app lain.
AppId={{A1F8E5D3-4B7C-4E9A-8D2B-6F1E3A5C7B9D}
AppName={#MyAppDisplayName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppDisplayName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
AppCopyright={#MyAppCopyright}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=LICENSE
OutputDir=installer-output
OutputBaseFilename=VPSMonitoringDesktop-Setup-{#MyAppVersion}
SetupIconFile=
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesInstallIn64BitMode=x64compatible
; Windows 10+ minimum
MinVersion=10.0
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppDisplayName}
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppDisplayName} Installer
VersionInfoCopyright={#MyAppCopyright}
ArchitecturesAllowed=x64compatible
; Tampilan modern
WizardSizePercent=120

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Source: folder publish hasil dotnet publish
Source: "bin\Release\net10.0-windows10.0.19041.0\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Jangan pakai "Flags: samefilename" — bisa konflik dengan file yang sudah ada

[Icons]
Name: "{group}\{#MyAppDisplayName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppDisplayName}}"; Filename: "{uninstallexe}"
Name: "{commondesktop}\{#MyAppDisplayName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Optional: launch app setelah install
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppDisplayName}}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Bersihkan log file di AppData saat uninstall (optional)
; Filename: "{cmd}"; Parameters: "/C del /q ""{localappdata}\Packages\VPS Monitor Desktop App\LocalState\update_logs.json"""; Flags: runhidden

[Code]
// Pre-installation check: pastikan .NET 10 runtime installed
function InitializeSetup(): Boolean;
var
  ResultCode: Integer;
begin
  // Cek apakah .NET 10 runtime terinstall via dotnet --list-runtimes
  if not Exec('cmd.exe', '/C dotnet --list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    // dotnet CLI tidak ada — anggap runtime .NET mungkin tidak terinstall
    // Tapi ini best-effort, kita tetap lanjut install (jika user punya runtime lain)
  end;

  Result := True; // Tetap lanjut install
end;

// Optional: pesan welcome dengan info prerequisites
function NeedAddPrerequisite(): Boolean;
begin
  Result := True;
end;
