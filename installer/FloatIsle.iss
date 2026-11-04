; FloatIsle 安装包（per-user，不签名，不写 HKLM）
; 编译：ISCC.exe FloatIsle.iss
; 前置：先 dotnet publish -c Release -r win-x64 --self-contained true -o publish

#define AppExeName "FloatIsle.exe"

; 版本号唯一真相源 = FloatIsle.csproj 里的 <Version>。SDK 把它写进 exe 的版本资源（ProductVersion），
; 这里直接读，脚本里不再手写第二份（以前写死 1.0.0，跟 README 自称的 v0.2 对不上）。
; 因此编译前必须先 dotnet publish，publish 目录里没有 exe 时这里会直接报错，不会静默用错版本。
#define AppVersion GetStringFileInfo("..\publish\" + AppExeName, "ProductVersion")

[Setup]
; 固定的 AppId：升级/卸载靠它识别，别改
AppId={{7E2A4C61-3B5D-4F0A-9C8E-5D1B2A6F7C34}
AppName=FloatIsle
AppVersion={#AppVersion}
AppVerName=FloatIsle {#AppVersion}
AppPublisher=FloatIsle
; per-user：装到 %LOCALAPPDATA%\Programs\FloatIsle，全程不需要管理员
DefaultDirName={localappdata}\Programs\FloatIsle
DisableDirPage=auto
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=Output
OutputBaseFilename=FloatIsle-Setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
; 升级时如果程序还在跑，提示关闭而不是强杀
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Messages]
; 不签名，首次运行会被 SmartScreen 拦，安装界面先把话说明白
WelcomeLabel2=This will install [name/ver] on your computer.%n%n注意：本程序没有代码签名。首次运行时 Windows 可能弹出「Windows 已保护你的电脑」，请点「更多信息」→「仍要运行」。%n%n安装位置是当前用户目录，不需要管理员权限。

[Tasks]
Name: "autostart"; Description: "开机时自动启动 FloatIsle（写入 HKCU 的 Run 项，可随时在托盘菜单里关掉）"; GroupDescription: "可选"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
; 开始菜单快捷方式（当前用户）
Name: "{userprograms}\FloatIsle"; Filename: "{app}\{#AppExeName}"; Comment: "FloatIsle 灵动岛"

[Registry]
; 勾了「开机自启」才写；卸载时删掉这个值（uninsdeletevalue）
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "FloatIsle"; ValueData: """{app}\{#AppExeName}"""; Flags: uninsdeletevalue; Tasks: autostart

[Run]
; 装完可选直接启动（默认勾上）
Filename: "{app}\{#AppExeName}"; Description: "立即启动 FloatIsle"; Flags: nowait postinstall skipifsilent

[Code]
// 不管安装时有没有勾自启，卸载都把 Run 里的 FloatIsle 值清干净
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'FloatIsle');
  end;
end;
