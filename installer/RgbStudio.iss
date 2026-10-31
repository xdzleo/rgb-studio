; RGB Studio installer (Inno Setup 6). Build with installer\build.ps1.
; The app needs administrator rights anyway (the Aura SDK does), so it installs for
; every user under Program Files.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\publish\sc"
#endif

[Setup]
AppId={{6F3B2C1E-8A4D-4E7B-9C25-5D1F0A9E7B42}
AppName=RGB Studio
AppVersion={#AppVersion}
AppVerName=RGB Studio {#AppVersion}
AppPublisher=xdzleo
AppPublisherURL=https://github.com/xdzleo/rgb-studio
AppSupportURL=https://github.com/xdzleo/rgb-studio/issues
DefaultDirName={autopf}\RGB Studio
DefaultGroupName=RGB Studio
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\publish
OutputBaseFilename=RgbStudio-{#AppVersion}-setup
SetupIconFile=..\RgbStudio\app.ico
UninstallDisplayIcon={app}\RgbStudio.exe
UninstallDisplayName=RGB Studio
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; The app is closed by [Code] below, which gives the lights back first.
CloseApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\RgbStudio.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\RGB Studio"; Filename: "{app}\RgbStudio.exe"; Comment: "Efeitos de luz para ASUS Aura"
Name: "{autodesktop}\RGB Studio"; Filename: "{app}\RgbStudio.exe"; Comment: "Efeitos de luz para ASUS Aura"; Tasks: desktopicon

[Run]
Filename: "{app}\RgbStudio.exe"; Description: "{cm:LaunchProgram,RGB Studio}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; "Iniciar com o Windows" is a scheduled task (see Autostart.cs); without the app it would point at nothing.
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""RGB Studio"" /F"; Flags: runhidden; RunOnceId: "DelAutostart"

; Settings and the log stay in %APPDATA%\RgbStudio after uninstalling, so a reinstall keeps your effects.

[Code]
function IsAppRunning(): Boolean;
var
  Wmi, Procs: Variant;
begin
  Result := False;
  try
    Wmi := CreateOleObject('WbemScripting.SWbemLocator');
    Procs := Wmi.ConnectServer('.', 'root\CIMV2').ExecQuery('SELECT ProcessId FROM Win32_Process WHERE Name = ''RgbStudio.exe''');
    Result := Procs.Count > 0;
  except
  end;
end;

// Asks a running RGB Studio to quit the normal way, which hands the lights back to
// Armoury Crate. If it does not, it is ended and LightingService is started again,
// since the app may have stopped it.
procedure CloseRgbStudio(const Exe: String);
var
  Code, I: Integer;
begin
  if not IsAppRunning() then Exit;
  if FileExists(Exe) then
    Exec(Exe, '--quit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  for I := 1 to 30 do
  begin
    if not IsAppRunning() then Exit;
    Sleep(500);
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM RgbStudio.exe', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\sc.exe'), 'start LightingService', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(1000);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRgbStudio(ExpandConstant('{app}\RgbStudio.exe'));
  if IsAppRunning() then
    Result := 'O RGB Studio ainda está aberto. Feche-o pela bandeja (Sair) e tente de novo.'
  else
    Result := '';
end;

// If "Iniciar com o Windows" was on for an older copy, point it at this one.
procedure CurStepChanged(CurStep: TSetupStep);
var
  Code: Integer;
begin
  if CurStep <> ssPostInstall then Exit;
  if Exec(ExpandConstant('{sys}\schtasks.exe'), '/Query /TN "RGB Studio"', '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0) then
    Exec(ExpandConstant('{sys}\schtasks.exe'),
      '/Create /TN "RGB Studio" /TR "\"' + ExpandConstant('{app}\RgbStudio.exe') + '\" --minimized" /SC ONLOGON /RL HIGHEST /F',
      '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

function InitializeUninstall(): Boolean;
begin
  CloseRgbStudio(ExpandConstant('{app}\RgbStudio.exe'));
  Result := not IsAppRunning();
  if not Result then
    MsgBox('O RGB Studio ainda está aberto. Feche-o pela bandeja (Sair) e tente de novo.', mbError, MB_OK);
end;
