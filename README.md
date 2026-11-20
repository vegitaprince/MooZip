# MooZip 1.1.0

Windows x64용 한국어 압축 파일 관리자입니다. MooView의 회색 패널, 왼쪽 도구막대, 밝은 테마와 어두운 테마를 사용합니다.

[Windows x64 설치 파일](https://github.com/vegitaprince/MooZip/releases/download/v1.1.0/MooZip-Setup-1.1.0-x64.exe) · [모든 배포 파일](https://github.com/vegitaprince/MooZip/releases/latest)

## 탐색기에서 압축하고 풀기

설치 프로그램의 **탐색기 우클릭 메뉴** 옵션은 기본으로 켜져 있습니다. Windows 10에서는 파일이나 폴더를 우클릭하세요. Windows 11에서는 우클릭한 뒤 **더 많은 옵션 표시**를 선택하세요.

- 파일과 폴더를 하나 이상 선택하고 **MooZip으로 압축…**을 누르면 선택한 항목을 한 압축 파일로 묶을 수 있습니다.
- ZIP·7Z·RAR·TAR 파일 하나를 우클릭하고 **MooZip**을 열면 **알아서 풀기**, **여기에 풀기**, **폴더를 선택하여 풀기…**, **MooZip으로 열기**를 사용할 수 있습니다.
- 설치 시 네 가지 형식의 기본 앱 후보로 MooZip을 등록합니다. MooZip의 **기본 앱 설정…**을 누른 뒤 Windows 설정에서 원하는 확장자에 MooZip을 선택하세요. Windows 10에서는 기본 앱 설정에서 파일 형식별 연결로 이동하세요.
- 무설치판에서도 **기본 앱 설정…**으로 파일 연결을 등록할 수 있습니다. 이때 실행 파일을 계속 사용할 폴더에 두세요. 탐색기 우클릭 메뉴는 설치판에 포함됩니다.

## 사용

- 홈에 ZIP, 7Z, RAR, TAR 파일을 놓으면 내용을 볼 수 있습니다. 일반 파일이나 폴더를 놓으면 새 압축 창이 열립니다.
- 새로 압축 창에서 여러 위치의 파일과 폴더를 추가한 뒤 ZIP 또는 7Z로 저장하세요. ZIP은 압축 수준을 선택할 수 있습니다.
- 알아서 풀기는 압축 파일이 있는 위치에 풉니다. 최상위 항목이 여러 개면 압축 파일 이름의 폴더를 만듭니다. 출력할 항목과 기존 이름이 겹치면 새 항목에 번호를 붙입니다.
- 전체 풀기와 선택 풀기에서는 대상 폴더를 직접 고릅니다. 선택한 항목은 압축 안의 경로를 유지합니다.
- 암호가 걸린 파일은 열거나 풀 때 암호를 입력합니다. 새 압축 파일은 암호 없이 생성합니다.
- Ctrl+O로 열기, Ctrl+N으로 새로 압축, Ctrl+F로 검색합니다. 목록에서 Ctrl 또는 Shift를 누른 채 여러 항목을 선택할 수 있습니다.
- 최근 목록과 테마는 현재 사용자 계정의 LocalAppData 아래 MooZip 폴더에 저장합니다.

## 빌드

Windows x64에서 .NET 10 SDK, Visual Studio의 C++ 데스크톱 개발 도구와 Windows SDK, Inno Setup 6을 설치하고 실행하세요.

```powershell
.\build.ps1 -InnoCompiler "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
```

빌드된 앱은 artifacts/app에, 설치 파일은 artifacts/release에 생성됩니다. 압축 라이브러리의 라이선스는 ThirdParty.txt에 있습니다.
