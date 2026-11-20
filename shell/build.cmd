@echo off
setlocal
if "%~1"=="" exit /b 2
if not exist "%~1" mkdir "%~1"
where cl.exe >nul 2>nul
if errorlevel 1 (
    for /f "usebackq tokens=*" %%v in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do call "%%v\VC\Auxiliary\Build\vcvars64.bat" >nul
)
cl.exe /nologo /std:c++17 /EHsc /MT /O2 /utf-8 /DUNICODE /D_UNICODE /LD "%~dp0CompressCommand.cpp" /Fo"%~1\MooZipShell.obj" /link /def:"%~dp0MooZipShell.def" /out:"%~1\MooZipShell.dll" ole32.lib shlwapi.lib oleaut32.lib runtimeobject.lib
exit /b %errorlevel%
