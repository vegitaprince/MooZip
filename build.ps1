param([string] $CertificateThumbprint)
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
    & .\shell\build.cmd $native
    if ($LASTEXITCODE -ne 0) { throw "탐색기 확장 빌드 실패" }
    Copy-Item -LiteralPath (Join-Path $native "MooZipShell.dll") -Destination $app
    & .\packaging\Build-MenuPackage.ps1 -OutputDirectory $app -CertificateThumbprint $CertificateThumbprint
    Copy-Item -LiteralPath README.md -Destination $app
    New-Item -ItemType Directory -Force $release | Out-Null
    $zip = Join-Path $release "MooZip-Portable-$version-x64.zip"
    $temporary = Join-Path $release ("MooZip-"+[guid]::NewGuid()+".tmp")
    try {
        [IO.Compression.ZipFile]::CreateFromDirectory($app,$temporary,[IO.Compression.CompressionLevel]::Optimal,$true)
        [IO.File]::Move($temporary,$zip,$true)
    } finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary } }
} finally { Pop-Location }
