using System.Diagnostics;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

namespace MooZip;

internal static class ModernMenu
{
    [DllImport("kernelbase.dll", CharSet=CharSet.Unicode)]
    static extern int GetPackagePathByFullName2(string fullName, int pathType, ref uint length, StringBuilder? path);

    static string? RegisteredDirectory()
    {
        const string fullName = "vegitaprince.MooZip.Explorer_1.2.0.0_x64__hzwmrkffjnyry";
        uint length=0;
        if (GetPackagePathByFullName2(fullName,5,ref length,null)!=122 || length==0) return null;
        var path=new StringBuilder((int)length);
        return GetPackagePathByFullName2(fullName,5,ref length,path)==0
            ? Path.TrimEndingDirectorySeparator(path.ToString()) : null;
    }
    // Keep paths as environment data. No filename from Explorer is evaluated as a command.
    internal static void Configure(bool register)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        const string find = """
            $ErrorActionPreference='Stop'
            [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
            $name='vegitaprince.MooZip.Explorer'
            $existing=Get-AppxPackage -Name $name | Where-Object PackageFamilyName -eq 'vegitaprince.MooZip.Explorer_hzwmrkffjnyry'
            """;
        const string install = """
            $dir=$env:MOOZIP_PACKAGE_DIRECTORY
            if($existing -and $existing.Version -eq [version]'1.2.0.0' -and $env:MOOZIP_SAME_LOCATION -eq 'yes') { exit 0 }
            $msix=Join-Path $dir 'MooZipExplorer.msix'
            if(!(Test-Path -LiteralPath $msix)) { throw 'MooZipExplorer.msix 파일이 필요합니다.' }
            if($existing -and $existing.Version -gt [version]'1.2.0.0') { throw '더 최신 버전의 탐색기 메뉴가 이미 등록되어 있습니다.' }
            if($existing -and $existing.Version -eq [version]'1.2.0.0') {
                if((Get-AuthenticodeSignature -LiteralPath $msix).Status -ne 'Valid') {throw '새 위치의 메뉴 패키지 서명을 확인하세요.'}
                Remove-AppxPackage -Package $existing.PackageFullName
                try {
                    Add-AppxPackage -Path $msix -ExternalLocation $dir
                } catch {
                    $failure=$_
                    $previous=$env:MOOZIP_PREVIOUS_DIRECTORY
                    if($previous) {
                        $previousPackage=Join-Path $previous 'MooZipExplorer.msix'
                        if(Test-Path -LiteralPath $previousPackage) {
                            try { Add-AppxPackage -Path $previousPackage -ExternalLocation $previous }
                            catch { [Console]::Error.WriteLine($_.Exception.Message) }
                        }
                    }
                    throw $failure
                }
                exit 0
            }
            Add-AppxPackage -Path $msix -ExternalLocation $dir
            """;
        const string remove = "if($existing){ Remove-AppxPackage -Package $existing.PackageFullName }";
        string script = "try {\n" + find + "\n" + (register ? install : remove) +
                        "\n} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        var start = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"))
        {
            UseShellExecute=false, CreateNoWindow=true, RedirectStandardError=true, StandardErrorEncoding=Encoding.UTF8
        };
        string directory=Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string? previous=RegisteredDirectory();
        start.Environment["MOOZIP_PACKAGE_DIRECTORY"]=directory;
        start.Environment["MOOZIP_PREVIOUS_DIRECTORY"]=previous ?? "";
        start.Environment["MOOZIP_SAME_LOCATION"]=string.Equals(previous,directory,StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
        foreach (string arg in new[]{"-NoProfile","-NonInteractive","-EncodedCommand",
                 Convert.ToBase64String(Encoding.Unicode.GetBytes(script))}) start.ArgumentList.Add(arg);
        using var process=Process.Start(start) ?? throw new IOException("Windows 패키지 등록 도구를 실행할 수 없습니다.");
        var error=process.StandardError.ReadToEnd();
        process.WaitForExit();
        if(process.ExitCode != 0)
            throw new IOException("Windows 11 탐색기 메뉴 등록을 변경하지 못했습니다.\n\n"+error.Trim());
    }
}

