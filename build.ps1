param([string] $InnoCompiler = "ISCC.exe")
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    $artifacts = Join-Path $PSScriptRoot "artifacts"
    $app = Join-Path $artifacts "app"
    $native = Join-Path $artifacts "shell"
    dotnet publish MooZip.csproj -c Release -r win-x64 --self-contained true -o $app
    if ($LASTEXITCODE -ne 0) { throw "앱 빌드 실패" }
    & .\shell\build.cmd $native
    if ($LASTEXITCODE -ne 0) { throw "탐색기 확장 빌드 실패" }
    Copy-Item -LiteralPath (Join-Path $native "MooZipShell.dll") -Destination $app
    & $InnoCompiler .\packaging\MooZip.iss
    if ($LASTEXITCODE -ne 0) { throw "설치 프로그램 빌드 실패" }
} finally { Pop-Location }
