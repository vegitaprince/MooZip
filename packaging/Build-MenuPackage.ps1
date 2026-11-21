param([Parameter(Mandatory)][string]$OutputDirectory, [string]$CertificateThumbprint)
$ErrorActionPreference = 'Stop'
$inputDir = Join-Path $PSScriptRoot 'modern'
$outputDir = (Resolve-Path -LiteralPath $OutputDirectory).Path
$kits = Join-Path "${env:ProgramFiles(x86)}" 'Windows Kits\10\bin'
$sdk = Get-ChildItem -LiteralPath $kits -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk) { throw 'Windows SDK x64 tools are required.' }
$package = Join-Path $outputDir 'MooZipExplorer.msix'
& (Join-Path $sdk.FullName 'x64\makeappx.exe') pack /o /nv /d $inputDir /p $package
if ($LASTEXITCODE) { throw 'Menu packaging failed.' }
$sign = Join-Path $sdk.FullName 'x64\signtool.exe'
if ($CertificateThumbprint) {
    & $sign sign /fd SHA256 /sha1 $CertificateThumbprint $package
    if ($LASTEXITCODE) { throw 'Menu signing failed.' }
} else {
    # One-use, non-CA code-signing key. Only the public certificate is distributed.
    $key = [Security.Cryptography.RSA]::Create(3072)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new('CN=MooZip Explorer',$key,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $usage = [Security.Cryptography.OidCollection]::new()
    [void]$usage.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usage,$true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,$true))
    $cert = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1),[DateTimeOffset]::UtcNow.AddYears(3))
    $password = [Convert]::ToHexString([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ('MooZip-'+[guid]::NewGuid()+'.pfx')
    try {
        [IO.File]::WriteAllBytes($temporary,$cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password))
        & $sign sign /fd SHA256 /f $temporary /p $password $package
        if ($LASTEXITCODE) { throw 'Menu signing failed.' }
        [IO.File]::WriteAllBytes((Join-Path $outputDir 'MooZipExplorer.cer'),$cert.RawData)
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary }
        $cert.Dispose()
        $key.Dispose()
    }
}

