#ifndef PublishDir
#define PublishDir "..\artifacts\app"
#endif
#ifndef ReleaseDir
#define ReleaseDir "..\artifacts\release"
#endif
[Setup]
AppId={{E802A95A-AE02-4F71-9293-E17B94473463}
AppName=MooZip
AppVersion=1.1.0
AppPublisher=MooZip
DefaultDirName={localappdata}\Programs\MooZip
DefaultGroupName=MooZip
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir={#ReleaseDir}
OutputBaseFilename=MooZip-Setup-1.1.0-x64
SetupIconFile=..\Assets\MooZip.ico
UninstallDisplayIcon={app}\MooZip.exe
Compression=lzma2/fast
SolidCompression=yes
WizardStyle=modern
RestartApplications=no
ChangesAssociations=yes
[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 바로 가기 만들기"; Flags: unchecked
Name: "explorermenu"; Description: "탐색기 우클릭 메뉴에 MooZip 압축 및 풀기 추가"
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{group}\MooZip"; Filename: "{app}\MooZip.exe"
Name: "{autodesktop}\MooZip"; Filename: "{app}\MooZip.exe"; Tasks: desktopicon
[Run]
Filename: "{app}\MooZip.exe"; Description: "MooZip 실행"; Flags: postinstall nowait skipifsilent
Filename: "{app}\MooZip.exe"; Parameters: "--defaults"; Description: "압축 파일의 기본 앱 설정 열기"; Flags: postinstall nowait skipifsilent unchecked
[UninstallRun]
Filename: "{app}\MooZip.exe"; Parameters: "--unregister"; Flags: runhidden waituntilterminated skipifdoesntexist; RunOnceId: "MooZipRemoveIntegration"
[Code]
procedure CurStepChanged(CurStep: TSetupStep);
var
  Argument: String;
  ResultCode: Integer;
begin
  if CurStep <> ssPostInstall then exit;
  if WizardIsTaskSelected('explorermenu') then Argument := '--install-menus'
  else Argument := '--install-formats';
  if not Exec(ExpandConstant('{app}\MooZip.exe'), Argument, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or (ResultCode <> 0) then
    RaiseException('Windows 탐색기 등록을 완료하지 못했습니다. 설치 프로그램을 다시 실행해 주세요.');
end;
