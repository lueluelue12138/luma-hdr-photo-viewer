#ifndef PayloadDir
  #error PayloadDir is required
#endif
#ifndef ReleaseVersion
  #error ReleaseVersion is required
#endif
#ifndef FileVersion
  #error FileVersion is required
#endif
#ifndef BuildVariant
  #error BuildVariant is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef FileAssociationsFile
  #error FileAssociationsFile is required
#endif
#ifndef RepositoryUrl
  #define RepositoryUrl "https://github.com/lueluelue12138/luma-hdr-photo-viewer"
#endif
#ifdef TestInstall
  #define ProductName "Luma Installer Test"
  #define ProductId "{{BB91BFB9-16F6-4FFB-8655-21B0156CBE91}"
  #define RegistrationName "Luma.InstallerTest"
  #define ProgId "Luma.InstallerTest.Photo"
  #define CapabilityKey "Software\Luma.InstallerTest\Capabilities"
  #define OutputName "Luma-installer-test"
#else
  #define ProductName "Luma HDR Photo Viewer"
  #define ProductId "{{0E473918-221F-45FC-A413-6145712ADFE5}"
  #define RegistrationName "Luma"
  #define ProgId "Luma.Photo"
  #define CapabilityKey "Software\Luma\Capabilities"
  #define OutputName "Luma-" + ReleaseVersion + "-windows-x64-" + BuildVariant + "-setup"
#endif

[Setup]
AppId={#ProductId}
AppName={#ProductName}
AppVersion={#ReleaseVersion}
AppVerName={#ProductName} {#ReleaseVersion}
AppPublisher=Luma contributors
AppPublisherURL={#RepositoryUrl}
AppSupportURL={#RepositoryUrl}/issues
AppUpdatesURL={#RepositoryUrl}/releases
VersionInfoVersion={#FileVersion}
VersionInfoDescription={#ProductName} installer ({#BuildVariant})
DefaultDirName={localappdata}\Programs\Luma
DefaultGroupName={#ProductName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
SetupIconFile={#PayloadDir}\Assets\Luma.ico
UninstallDisplayIcon={app}\Luma.App.exe
#ifdef RtxLicenseFile
LicenseFile={#RtxLicenseFile}
#else
LicenseFile={#PayloadDir}\LICENSE
#endif
Compression=lzma2/normal
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
ChangesAssociations=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,AppxManifest.xml"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#ProductName}"; Filename: "{app}\Luma.App.exe"
Name: "{autodesktop}\{#ProductName}"; Filename: "{app}\Luma.App.exe"; Tasks: desktopicon

; Only register supported formats as an available choice. Do not write
; UserChoice or change any extension's default program.
[Registry]
Root: HKCU; Subkey: "Software\RegisteredApplications"; ValueType: string; ValueName: "{#RegistrationName}"; ValueData: "{#CapabilityKey}"
Root: HKCU; Subkey: "{#CapabilityKey}"; ValueType: string; ValueName: "ApplicationName"; ValueData: "{#ProductName}"
Root: HKCU; Subkey: "{#CapabilityKey}"; ValueType: string; ValueName: "ApplicationDescription"; ValueData: "Windows photo viewer with native HDR and optional RTX Video HDR / VSR"
Root: HKCU; Subkey: "{#CapabilityKey}"; ValueType: string; ValueName: "ApplicationIcon"; ValueData: """{app}\Luma.App.exe"",0"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}"; ValueType: string; ValueName: ""; ValueData: "Luma photo"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: """{app}\Luma.App.exe"",0"
Root: HKCU; Subkey: "Software\Classes\{#ProgId}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Luma.App.exe"" ""%1"""
#include FileAssociationsFile

[Run]
Filename: "{app}\Luma.App.exe"; Description: "Open Luma"; Flags: nowait postinstall skipifsilent

[InstallDelete]
; Switching to a standard build removes only these known RTX output files.
#if BuildVariant == "standard"
Type: files; Name: "{app}\nvngx_truehdr.dll"
Type: files; Name: "{app}\nvngx_vsr.dll"
Type: files; Name: "{app}\NVIDIA_RTX_Video_SDK_License.pdf"
#endif

; No UninstallDelete entry: originals and %LOCALAPPDATA%\Luma remain intact.

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  CurrentCommand, Registration: String;
begin
  if CurUninstallStep <> usUninstall then Exit;
  // A portable copy may have re-registered Luma after installation. Keep that
  // copy's registration rather than deleting it during this uninstall.
  if not RegQueryStringValue(HKCU, 'Software\Classes\{#ProgId}\shell\open\command', '', CurrentCommand) then Exit;
  if CompareText(CurrentCommand, '"' + ExpandConstant('{app}\Luma.App.exe') + '" "%1"') <> 0 then Exit;
  if RegQueryStringValue(HKCU, 'Software\RegisteredApplications', '{#RegistrationName}', Registration) then
    if CompareText(Registration, '{#CapabilityKey}') = 0 then
      RegDeleteValue(HKCU, 'Software\RegisteredApplications', '{#RegistrationName}');
  RegDeleteKeyIncludingSubkeys(HKCU, '{#CapabilityKey}');
  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\{#ProgId}');
  // Generated from exactly the same supported-format manifest as install.
  #include UninstallAssociationsFile
end;
