; ClipboardX Inno Setup Script
; 用法: iscc /DAppVersion=1.2.0 /DPublishDir=..\publish\sc clipboardx.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish\sc"
#endif

[Setup]
AppId={{E2F6D4A8-7B1C-4D3E-9F0A-5C8B2E1D6A7F}
AppName=ClipboardX
AppVersion={#AppVersion}
AppVerName=ClipboardX {#AppVersion}
AppPublisher=ClipboardX
AppPublisherURL=https://github.com/chaojimct/clipboardx
DefaultDirName={userpf}\ClipboardX
DefaultGroupName=ClipboardX
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputBaseFilename=ClipboardX-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\ClipboardX.exe
SetupIconFile=..\assets\clipboard.ico
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=force
RestartApplications=no

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "runonstartup"; Description: "开机自动启动"; GroupDescription: "其他选项:"; Flags: checked

[Files]
Source: "{#PublishDir}\ClipboardX.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\ClipboardX"; Filename: "{app}\ClipboardX.exe"
Name: "{group}\卸载 ClipboardX"; Filename: "{uninstallexe}"
Name: "{autodesktop}\ClipboardX"; Filename: "{app}\ClipboardX.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ClipboardX.exe"; Description: "启动 ClipboardX"; Flags: nowait postinstall skipifsilent

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "ClipboardX"; ValueData: """{app}\ClipboardX.exe"""; Tasks: runonstartup; Flags: uninsdeletevalue

[UninstallRun]
Filename: "taskkill"; Parameters: "/f /im ClipboardX.exe"; Flags: runhidden; RunOnceId: "KillClipboardX"

[UninstallDelete]
Type: filesandordirs; Name: "{app}"
