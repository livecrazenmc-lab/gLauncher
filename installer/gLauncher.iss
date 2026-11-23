; Установщик gLauncher (Inno Setup 6).
;
; Сборка (из корня репозитория, после dotnet publish в папку publish):
;   ISCC.exe /DAppVersion=0.3.0 installer\gLauncher.iss
; Результат: installer\Output\gLauncher-Setup.exe
;
; Устанавливается для текущего пользователя без прав администратора в %LocalAppData%\Programs\gLauncher.
; Папка доступна пользователю на запись — это нужно автообновлению, которое подменяет gLauncher.exe на месте.
; Данные лаунчера (%APPDATA%\.glauncher: аккаунты, профили, миры) при удалении НЕ трогаются.

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

#define AppName "gLauncher"
#define AppExe "gLauncher.exe"
#define AppPublisher "livecrazenmc-lab"
#define AppUrl "https://github.com/livecrazenmc-lab/gLauncher"

[Setup]
; Не меняйте AppId: по нему установщик находит прошлую установку при обновлении.
AppId={{6B0E4C2A-9D1F-4E7B-A3C5-2F8D61B7E940}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#AppVersion}

PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
UsePreviousAppDir=yes

OutputDir=Output
OutputBaseFilename=gLauncher-Setup
SetupIconFile=..\src\GLauncher\Assets\gLauncher.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}

Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763

; Закрыть запущенный лаунчер перед обновлением файлов.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Временные файлы автообновления (gLauncher.exe.old / .new).
Type: files; Name: "{app}\{#AppExe}.old"
Type: files; Name: "{app}\{#AppExe}.new"
Type: dirifempty; Name: "{app}"
