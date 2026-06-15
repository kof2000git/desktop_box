; DesktopBox Inno Setup 安装脚本
; 用法:用 ISCC.exe 编译本文件,生成 release\DesktopBoxSetup.exe

[Setup]
AppName=DesktopBox
AppVersion=1.5.0
AppVerName=DesktopBox 1.5.0
AppPublisher=DesktopBox
DefaultDirName={localappdata}\Programs\DesktopBox
DefaultGroupName=DesktopBox
DisableProgramGroupPage=yes
DisableDirPage=yes
UninstallDisplayIcon={app}\DesktopBox.exe
OutputDir=release
OutputBaseFilename=DesktopBoxSetup
Compression=lzma2/ultra64
SolidCompression=yes
LZMAUseSeparateProcess=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
WizardStyle=modern
CompressionThreads=auto

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"

[Files]
Source: "publish\DesktopBox.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\DesktopBox"; Filename: "{app}\DesktopBox.exe"
Name: "{group}\Uninstall DesktopBox"; Filename: "{uninstallexe}"
Name: "{autodesktop}\DesktopBox"; Filename: "{app}\DesktopBox.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DesktopBox.exe"; Description: "Launch DesktopBox"; Flags: nowait postinstall skipifsilent
