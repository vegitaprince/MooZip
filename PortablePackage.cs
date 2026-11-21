using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MooZip;

internal sealed record PayloadFile(string Path, long Size, string Sha256);
internal sealed record PayloadManifest(string Version, PayloadFile[] Files);

internal static class PortablePackage
{
    internal const string ManifestName = "MooZip.files.json";
    const string Prefix = "MooZip/";
    const int ManifestLimit = 1024 * 1024;
    const long ExpandedLimit = 1024L * 1024 * 1024;
    static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    static readonly string[] Required = ["MooZip.exe", "MooZip.dll", "MooZip.deps.json", "MooZip.runtimeconfig.json", "coreclr.dll", "MooZipShell.dll", "MooZipExplorer.msix"];
    static readonly string[] Extras = ["MooZip.exe", "MooZip.deps.json", "MooZip.runtimeconfig.json", "createdump.exe",
        "README.md", "ThirdParty.txt", "MooZipExplorer.msix", "MooZipExplorer.cer"];

    static bool Allowed(string path)
    {
        var parts = path.Split('/');
        if (parts.Length > 2 || parts.Any(part =>
            !Regex.IsMatch(part, @"\A[a-zA-Z0-9][a-zA-Z0-9_.-]*\z") || part.EndsWith('.') ||
            Regex.IsMatch(part, @"\A(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])(\.|$)", RegexOptions.IgnoreCase)))
            return false;
        if (parts.Length == 1)
            return Extras.Contains(path, Names) || path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        return Regex.IsMatch(parts[0], @"\A[a-z]{2,3}(-[a-zA-Z0-9]{2,8}){0,2}\z") &&
               parts[1].EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);
    }

    internal static void RejectLinks(string path)
    {
        for (string? current = path; current != null; current = Path.GetDirectoryName(current))
        {
            try
            {
                if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("링크가 포함된 경로에서는 업데이트할 수 없습니다. " + current);
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

    static string Target(string root, string relative)
    {
        if (relative != ManifestName && !Allowed(relative)) throw new InvalidDataException("잘못된 업데이트 파일 경로입니다.");
        string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        RejectLinks(path);
        return path;
    }

    static PayloadManifest ParseManifest(string json, Version version)
    {
        var manifest = JsonSerializer.Deserialize<PayloadManifest>(json);
        if (manifest?.Version != version.ToString() || manifest.Files == null || manifest.Files.Length > 2000)
            throw new InvalidDataException("포터블 파일 목록 또는 버전이 올바르지 않습니다.");
        var names = new HashSet<string>(Names);
        long length = 0;
        foreach (var file in manifest.Files)
        {
            if (file?.Path == null || !Allowed(file.Path) || !names.Add(file.Path) ||
                file.Size is < 0 or > ExpandedLimit || file.Sha256 == null || !Regex.IsMatch(file.Sha256, @"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("포터블 파일 목록의 항목이 올바르지 않습니다.");
            length += file.Size;
            if (length > ExpandedLimit) throw new InvalidDataException("업데이트가 허용된 크기를 초과합니다.");
        }
        if (!Required.All(names.Contains)) throw new InvalidDataException("ZIP에 필요한 프로그램 파일이 없습니다.");
        return manifest;
    }

    static string ReadManifest(string path)
    {
        if (new FileInfo(path).Length > ManifestLimit) throw new InvalidDataException("포터블 파일 목록이 너무 큽니다.");
        return File.ReadAllText(path);
    }

    static string Hash(string path)
    {
        using var file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }

    static void CheckExecutable(string directory, Version version)
    {
        var info = FileVersionInfo.GetVersionInfo(Target(directory, "MooZip.exe"));
        if (info.ProductName != "MooZip" || info.ProductVersion?.Split('+')[0] != version.ToString())
            throw new InvalidDataException("업데이트 실행 파일의 버전이 일치하지 않습니다.");
    }

    internal static void Create(string directory, string output)
    {
        var candidates = Directory.EnumerateFiles(directory)
            .Concat(Directory.EnumerateDirectories(directory).SelectMany(child => Directory.EnumerateFiles(child)));
        var files = candidates.Select(path => Path.GetRelativePath(directory, path).Replace('\\', '/'))
            .Where(Allowed).OrderBy(path => path, StringComparer.Ordinal).Select(relative =>
            {
                string path = Target(directory, relative);
                return new PayloadFile(relative, new FileInfo(path).Length, Hash(path));
            }).ToArray();
        var version = UpdateClient.CurrentVersion;
        string json = JsonSerializer.Serialize(new PayloadManifest(version.ToString(), files));
        ParseManifest(json, version);
        CheckExecutable(directory, version);
        File.WriteAllText(Target(directory, ManifestName), json);
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
                foreach (string path in files.Select(file => file.Path).Append(ManifestName))
                    zip.CreateEntryFromFile(Target(directory, path), Prefix + path, CompressionLevel.Optimal);
            File.Move(temporary, output, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    internal static async Task<string> StageAsync(string archive, string stage, Version version, CancellationToken token)
    {
        Directory.CreateDirectory(stage);
        RejectLinks(stage);
        using var zip = ZipFile.OpenRead(archive);
        var entries = new Dictionary<string, ZipArchiveEntry>(Names);
        foreach (var entry in zip.Entries)
        {
            int attributes = entry.ExternalAttributes;
            if (!entry.FullName.StartsWith(Prefix, StringComparison.Ordinal) ||
                ((attributes >> 16) & 0xF000) == 0xA000 || (attributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("ZIP에 지원하지 않는 항목이 있습니다.");
            string name = entry.FullName[Prefix.Length..];
            if ((name != ManifestName && !Allowed(name)) || !entries.TryAdd(name, entry))
                throw new InvalidDataException("ZIP에 잘못되었거나 중복된 항목이 있습니다.");
        }

        if (!entries.TryGetValue(ManifestName, out var manifestEntry) || manifestEntry.Length > ManifestLimit)
            throw new InvalidDataException("ZIP에 포터블 파일 목록이 없습니다.");
        using var manifestStream = manifestEntry.Open();
        using var manifestBuffer = new MemoryStream();
        await UpdateClient.CopyAsync(manifestStream, manifestBuffer, ManifestLimit, null, null, token);
        string json = System.Text.Encoding.UTF8.GetString(manifestBuffer.ToArray());
        var manifest = ParseManifest(json, version);
        if (entries.Count != manifest.Files.Length + 1) throw new InvalidDataException("ZIP의 파일 목록이 일치하지 않습니다.");

        foreach (var file in manifest.Files)
        {
            if (!entries.TryGetValue(file.Path, out var entry) || entry.Length != file.Size)
                throw new InvalidDataException("ZIP의 파일 크기가 일치하지 않습니다. " + file.Path);
            string destination = Target(stage, file.Path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await using var input = entry.Open();
            await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long length = await UpdateClient.CopyAsync(input, output, file.Size, hash, null, token);
            if (length != file.Size || !Names.Equals(Convert.ToHexString(hash.GetHashAndReset()), file.Sha256))
                throw new InvalidDataException("ZIP 안의 파일 검증에 실패했습니다. " + file.Path);
        }
        File.WriteAllText(Target(stage, ManifestName), json);
        CheckExecutable(stage, version);
        return Convert.ToHexString(SHA256.HashData(manifestBuffer.ToArray()));
    }

    internal static void VerifyStage(string stage, Version version, string manifestHash)
    {
        string path = Target(stage, ManifestName);
        if (!Names.Equals(Hash(path), manifestHash)) throw new InvalidDataException("준비된 업데이트 목록이 변경되었습니다.");
        var manifest = ParseManifest(ReadManifest(path), version);
        var expected = manifest.Files.Select(file => file.Path).Append(ManifestName).ToHashSet(Names);
        var files = Directory.EnumerateFiles(stage).ToList();
        foreach (string child in Directory.EnumerateDirectories(stage))
        {
            RejectLinks(child);
            if (Directory.EnumerateDirectories(child).Any()) throw new InvalidDataException("준비된 업데이트 폴더가 변경되었습니다.");
            files.AddRange(Directory.EnumerateFiles(child));
        }
        if (!expected.SetEquals(files.Select(file => Path.GetRelativePath(stage, file).Replace('\\', '/'))))
            throw new InvalidDataException("준비된 업데이트 파일 목록이 변경되었습니다.");
        foreach (var file in manifest.Files)
        {
            string target = Target(stage, file.Path);
            if (new FileInfo(target).Length != file.Size || !Names.Equals(Hash(target), file.Sha256))
                throw new InvalidDataException("준비된 업데이트 파일이 변경되었습니다. " + file.Path);
        }
    }

    internal static void Apply(string stage, string target, Version fromVersion, Version toVersion)
    {
        string json = ReadManifest(Target(stage, ManifestName));
        var incoming = ParseManifest(json, toVersion);
        var existing = ParseManifest(ReadManifest(Target(target, ManifestName)), fromVersion);
        var owned = existing.Files.ToDictionary(file => file.Path, Names);
        var incomingNames = incoming.Files.Select(file => file.Path).ToHashSet(Names);
        CheckExecutable(stage, toVersion);
        CheckExecutable(target, fromVersion);

        string transaction = Path.Combine(target, ".moozip-update-" + Guid.NewGuid().ToString("N"));
        string replacement = Path.Combine(transaction, "next");
        string backups = Path.Combine(transaction, "backup");
        Directory.CreateDirectory(replacement);
        var changes = new List<(string Path, bool Install)>();
        var moved = new List<(string Destination, string Backup, bool Placed)>();
        bool rollbackSucceeded = true;
        try
        {
            foreach (var file in incoming.Files)
            {
                string destination = Target(target, file.Path);
                if (File.Exists(destination))
                {
                    string currentHash = Hash(destination);
                    if (Names.Equals(currentHash, file.Sha256)) continue;
                    if (!owned.TryGetValue(file.Path, out var old) || !Names.Equals(currentHash, old.Sha256))
                        throw new IOException("기존 파일을 보존하기 위해 업데이트를 중단했습니다. " + file.Path);
                }
                string next = Target(replacement, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(next)!);
                File.Copy(Target(stage, file.Path), next);
                if (new FileInfo(next).Length != file.Size || !Names.Equals(Hash(next), file.Sha256))
                    throw new InvalidDataException("준비된 업데이트 파일이 변경되었습니다. " + file.Path);
                changes.Add((file.Path, true));
            }
            foreach (var file in existing.Files)
            {
                if (incomingNames.Contains(file.Path)) continue;
                string destination = Target(target, file.Path);
                if (File.Exists(destination) && Names.Equals(Hash(destination), file.Sha256))
                    changes.Add((file.Path, false));
            }
            File.WriteAllText(Target(replacement, ManifestName), json);
            changes.Add((ManifestName, true));

            foreach (var (path, install) in changes)
            {
                string destination = Target(target, path);
                string backup = Target(backups, path);
                if (File.Exists(destination))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Move(destination, backup);
                }
                moved.Add((destination, backup, false));
                if (!install) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(Target(replacement, path), destination);
                moved[^1] = (destination, backup, true);
            }
            CheckExecutable(target, toVersion);
        }
        catch (Exception error)
        {
            foreach (var file in moved.AsEnumerable().Reverse())
            {
                try
                {
                    if (file.Placed) File.Delete(file.Destination);
                    if (File.Exists(file.Backup)) File.Move(file.Backup, file.Destination);
                }
                catch (IOException) { rollbackSucceeded = false; }
                catch (UnauthorizedAccessException) { rollbackSucceeded = false; }
            }
            if (!rollbackSucceeded) throw new IOException("업데이트 파일의 복구본을 보존했습니다. " + transaction, error);
            throw;
        }
        finally
        {
            if (rollbackSucceeded) TryClean(transaction);
        }
    }

    internal static void TryClean(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return;
            RejectLinks(path);
            foreach (var file in Directory.EnumerateFiles(path))
            {
                RejectLinks(file);
                File.Delete(file);
            }
            foreach (var child in Directory.EnumerateDirectories(path)) TryClean(child);
            Directory.Delete(path);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
