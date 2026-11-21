using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MooZip;

internal static class UpdateHandoff
{
    internal static string AppDirectory => Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
    internal static bool IsPortable => File.Exists(Path.Combine(AppDirectory, PortablePackage.ManifestName));

    internal static string StateDirectory(string appDirectory)
    {
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory)).ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..24];
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MooZip", "Updates", key);
    }

    static FileStream Acquire(string directory, bool exclusive, TimeSpan wait)
    {
        string state = StateDirectory(directory);
        Directory.CreateDirectory(state);
        PortablePackage.RejectLinks(state);
        string path = Path.Combine(state, "use.lock");
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                PortablePackage.RejectLinks(path);
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.Read,
                    exclusive ? FileShare.None : FileShare.Read);
            }
            catch (IOException) when (timer.Elapsed < wait) { Thread.Sleep(150); }
        }
    }

    internal static FileStream EnterApp() => Acquire(AppDirectory, false, TimeSpan.FromSeconds(20));

    internal static string NewDownload()
    {
        string work = Path.Combine(StateDirectory(AppDirectory), "download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        PortablePackage.RejectLinks(work);
        return work;
    }

    internal static void Start(string work, bool restart)
    {
        using var parent = Process.GetCurrentProcess();
        var helper = new ProcessStartInfo(Path.Combine(work, "payload", "MooZip.exe")) { UseShellExecute = false };
        foreach (string arg in new[] { "--apply-portable", AppDirectory, UpdateClient.CurrentVersion.ToString(),
                     restart ? "restart" : "exit", parent.Id.ToString(), parent.StartTime.ToUniversalTime().Ticks.ToString() })
            helper.ArgumentList.Add(arg);
        using var process = Process.Start(helper) ?? throw new IOException("업데이트 도우미를 실행할 수 없습니다.");
    }

    internal static int Apply(string target, string previousVersion, bool restart, int parentId, long parentStart)
    {
        var current = UpdateClient.CurrentVersion;
        var previous = Version.Parse(previousVersion);
        target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        string stagingRoot = Path.GetDirectoryName(AppDirectory)!;
        string state = StateDirectory(target);
        if (previous >= current || !Path.GetFileName(stagingRoot).StartsWith("download-", StringComparison.Ordinal) ||
            !Guid.TryParseExact(Path.GetFileName(stagingRoot)[9..], "N", out _) ||
            !string.Equals(AppDirectory, Path.Combine(stagingRoot, "payload"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetDirectoryName(stagingRoot), state, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("업데이트 위치 또는 버전이 올바르지 않습니다.");

        string exe = Path.Combine(target, "MooZip.exe");
        try
        {
            WaitForParent(parentId, parentStart, exe);
            using (Acquire(target, true, TimeSpan.FromMinutes(1)))
            {
                string? installed = FileVersionInfo.GetVersionInfo(exe).ProductVersion?.Split('+')[0];
                if (!Version.TryParse(installed, out var version)) throw new IOException("현재 버전을 확인할 수 없습니다.");
                if (version < current) PortablePackage.Apply(AppDirectory, target, version, current);
            }
            if (restart)
            {
                using var app = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });
            }
            return 0;
        }
        catch (Exception error)
        {
            try { File.WriteAllText(Path.Combine(state, "update-error.txt"), error.Message); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            throw;
        }
        finally
        {
            try { File.WriteAllText(Path.Combine(stagingRoot, "complete"), ""); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static void WaitForParent(int id, long start, string exe)
    {
        try
        {
            using var parent = Process.GetProcessById(id);
            if (parent.HasExited || parent.StartTime.ToUniversalTime().Ticks != start ||
                !string.Equals(parent.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase)) return;
            if (!parent.WaitForExit(30_000)) throw new IOException("업데이트를 시작하려면 MooZip 창을 닫아 주세요.");
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    internal static void Cleanup(string state)
    {
        if (!Directory.Exists(state)) return;
        foreach (string path in Directory.EnumerateDirectories(state, "download-*"))
        {
            if (!Guid.TryParseExact(Path.GetFileName(path)[9..], "N", out _)) continue;
            if (File.Exists(Path.Combine(path, "complete")) || Directory.GetLastWriteTimeUtc(path) < DateTime.UtcNow.AddDays(-7))
                PortablePackage.TryClean(path);
        }
    }
}
