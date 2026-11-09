; Windows installer of Raffica PDF (Inno Setup 6, pinned version in tools/package.cs).
; Built by: dotnet run tools/package.cs -- installer --layout <dir> --version <x.y.z> --out <file>
; which passes the defines below (/D...) plus the output folder and name (/O, /F) to iscc.exe.
; Keep this file ASCII: translated texts come from the .isl files of Inno Setup.
; The rules this script must keep are checked by `package.cs selftest` (IssRules).

#ifndef AppVersion
  #error AppVersion is not defined (call iscc through tools/package.cs)
#endif
#ifndef NumericVersion
  #error NumericVersion is not defined (call iscc through tools/package.cs)
#endif
#ifndef LayoutDir
  #error LayoutDir is not defined (call iscc through tools/package.cs)
#endif
#ifndef IconFile
  #error IconFile is not defined (call iscc through tools/package.cs)
#endif

[Setup]
; Generated once, never change it: it identifies the installation for updates and the uninstaller.
AppId={{E2602600-DBDA-4741-8773-A1D1284E2562}
AppName=Raffica PDF
AppVersion={#AppVersion}
AppPublisher=Raffica
VersionInfoVersion={#NumericVersion}
VersionInfoProductTextVersion={#AppVersion}
; User install by default (no elevation); the user may choose all users in a dialog or with /ALLUSERS.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog
; {autopf} is Program Files for all users and %LOCALAPPDATA%\Programs for the current user.
DefaultDirName={autopf}\Raffica PDF
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile={#LayoutDir}\LICENSE
CloseApplications=yes
ChangesAssociations=yes
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\RafficaPdf.exe
UninstallDisplayName=Raffica PDF
WizardStyle=modern dynamic
ShowLanguageDialog=auto
Compression=lzma2/max
SolidCompression=yes

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "{#LayoutDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Raffica PDF"; Filename: "{app}\RafficaPdf.exe"

[Run]
; Registers Raffica PDF for PDF files (ProgId, "Open with"); the default app question is asked by the app itself.
Filename: "{app}\RafficaPdf.exe"; Parameters: "--register-file-types={code:FileTypeScope}"; Flags: runhidden
Filename: "{app}\RafficaPdf.exe"; Description: "{cm:LaunchProgram,Raffica PDF}"; Flags: postinstall nowait skipifsilent

[UninstallRun]
; Runs as the first step of the uninstallation, before any file is removed. The settings in %APPDATA%\Raffica stay.
Filename: "{app}\RafficaPdf.exe"; Parameters: "--unregister-file-types={code:FileTypeScope}"; Flags: runhidden; RunOnceId: "UnregisterFileTypes"

[Code]
// Scope of the file type registration: HKLM for an installation for all users, HKCU otherwise.
// Expanded at install time, also for the [UninstallRun] entry (stored in the uninstall log).
function FileTypeScope(Param: String): String;
begin
  if IsAdminInstallMode then
    Result := 'machine'
  else
    Result := 'user';
end;
