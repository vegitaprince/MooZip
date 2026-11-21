param([string] $CertificateThumbprint, [string] $ReuseExplorerFrom)
$ErrorActionPreference = "Stop"
Push-Location $PSScriptRoot
try {
    $artifacts = Join-Path $PSScriptRoot "artifacts"
    $app = Join-Path $artifacts "MooZip"
    $native = Join-Path $artifacts "shell"
    $release = Join-Path $artifacts "release"
    $version = ([xml](Get-Content -LiteralPath MooZip.csproj -Raw)).Project.PropertyGroup.Version
    dotnet publish MooZip.csproj -c Release -r win-x64 --self-contained true -o $app
    if ($LASTEXITCODE -ne 0) { throw "앱 빌드 실패" }
    if ($ReuseExplorerFrom) {
        if ($CertificateThumbprint) { throw "기존 메뉴 패키지 재사용과 새 서명은 별도로 지정하세요." }
        foreach ($file in 'MooZipShell.dll','MooZipExplorer.msix','MooZipExplorer.cer') {
            Copy-Item -LiteralPath (Join-Path $ReuseExplorerFrom $file) -Destination $app
        }
    } else {
        & .\shell\build.cmd $native
        if ($LASTEXITCODE -ne 0) { throw "탐색기 확장 빌드 실패" }
        Copy-Item -LiteralPath (Join-Path $native "MooZipShell.dll") -Destination $app
        & .\packaging\Build-MenuPackage.ps1 -OutputDirectory $app -CertificateThumbprint $CertificateThumbprint
    }
    Copy-Item -LiteralPath README.md -Destination $app
    New-Item -ItemType Directory -Force $release | Out-Null
    $zip = Join-Path $release "MooZip-Portable-$version-x64.zip"
    $packager = [Diagnostics.ProcessStartInfo]::new((Join-Path $app "MooZip.exe"))
    $packager.UseShellExecute = $false
    $packager.ArgumentList.Add('--package-portable')
    $packager.ArgumentList.Add($zip)
    $process = [Diagnostics.Process]::Start($packager)
    $process.WaitForExit()
    if ($process.ExitCode) { throw "포터블 ZIP 생성 실패" }
} finally { Pop-Location }
