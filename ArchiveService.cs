using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Writers.SevenZip;

namespace MooZip;

public sealed record Member(string Path, bool Folder, long Size, DateTime? Modified, bool Encrypted)
{
    public string Name => System.IO.Path.GetFileName(Path);
    public string Location => System.IO.Path.GetDirectoryName(Path)?.Replace('\\','/') is {Length:>0} s ? s : "/";
    public string Kind => Folder ? "폴더" : System.IO.Path.GetExtension(Name).TrimStart('.').ToUpperInvariant() is {Length:>0} ext ? ext : "파일";
    public string Bytes => Folder ? "—" : ArchiveService.SizeText(Size);
    public string Symbol => Folder ? "\uE8B7" : "\uE8A5";
}
public sealed record Catalog(string FileName, Member[] Members, string Format, long Length, DateTime Stamp);
public sealed record JobInfo(string Name, double Fraction);
public static partial class ArchiveService
{
    static readonly StringComparer Names = StringComparer.OrdinalIgnoreCase;
    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|(?:LPT|COM)[0-9¹²³])(?:\.|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceName();
    public static string SizeText(long value)
    {
        double size = value;
        string[] units = ["B","KB","MB","GB","TB"];
        int i = 0;
        while(size >= 1024 && i < units.Length-1) { size /= 1024; i++; }
        return $"{size:0.##} {units[i]}";
    }
    static string SafeName(string? raw)
    {
        string key = (raw ?? "").Replace('\\','/').TrimEnd('/');
        while(key.StartsWith("./",StringComparison.Ordinal)) key = key[2..];
        var parts = key.Split('/');
        if(parts.Any(p => p.Length == 0 || p.EndsWith(' ') || p.EndsWith('.') || DeviceName().IsMatch(p) || p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Windows에 저장할 수 없는 항목 이름이 있습니다. " + raw);
        return key;
    }
    static ReaderOptions Options(string? password) => new() {
        Password = password, ArchiveEncoding = new ArchiveEncoding {Default = Encoding.GetEncoding(949)}
    };
    public static Catalog Read(string path, string? password, CancellationToken token)
    {
        using var archive = ArchiveFactory.OpenArchive(path, Options(password));
        List<Member> members = [];
        var explicitNames = new HashSet<string>(Names);
        var directories = new HashSet<string>(Names);
        var files = new HashSet<string>(Names);
        foreach(var e in archive.Entries)
        {
            token.ThrowIfCancellationRequested();
            if(e.IsDirectory && (e.Key == "." || e.Key == "./")) continue;
            var name = SafeName(e.Key);
            if(!explicitNames.Add(name) || (!e.IsDirectory && directories.Contains(name)))
                throw new InvalidDataException("같은 경로를 사용하는 항목이 있습니다. " + name);
            for(int i=name.IndexOf('/'); i>=0; i=name.IndexOf('/', i+1))
            {
                string parent=name[..i];
                if(files.Contains(parent)) throw new InvalidDataException("파일과 폴더 경로가 겹칩니다. " + parent);
                directories.Add(parent);
            }
            if(e.IsDirectory) directories.Add(name); else files.Add(name);
            if(e.LinkTarget != null) throw new InvalidDataException("링크 항목이 포함되어 있습니다. " + name);
            members.Add(new(name,e.IsDirectory,e.Size,e.LastModifiedTime,e.IsEncrypted));
        }
        var file = new FileInfo(path);
        return new(path,members.ToArray(),archive.Type == ArchiveType.SevenZip ? "7Z" : archive.Type.ToString().ToUpperInvariant(),file.Length,file.LastWriteTimeUtc);
    }
    sealed record Input(string Disk, string Name, bool Folder, long Size, DateTime Modified);
    static List<Input> Inventory(IEnumerable<string> sources, string destination, CancellationToken token)
    {
        var roots = sources.Select(Path.GetFullPath).Distinct(Names).ToArray();
        var used = new HashSet<string>(Names);
        var items = new List<Input>();
        void Visit(string path, string name)
        {
            token.ThrowIfCancellationRequested();
            if(Names.Equals(path,destination)) throw new IOException("압축 대상과 저장할 파일의 경로가 겹칩니다.");
            FileAttributes attrs = File.GetAttributes(path);
            if(attrs.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("링크로 연결된 파일이나 폴더는 추가할 수 없습니다. " + path);
            bool folder = attrs.HasFlag(FileAttributes.Directory);
            items.Add(new(path,name,folder,folder ? 0 : new FileInfo(path).Length, File.GetLastWriteTime(path)));
            if(folder)
                foreach(var child in Directory.EnumerateFileSystemEntries(path))
                    Visit(child, name + "/" + Path.GetFileName(child));
        }
        foreach(var source in roots)
        {
            if(roots.Any(parent => !Names.Equals(source,parent) && source.StartsWith(Path.TrimEndingDirectorySeparator(parent) + Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))) continue;
            var rootName = SafeName(Path.GetFileName(Path.TrimEndingDirectorySeparator(source)));
            if(!used.Add(rootName)) throw new IOException("서로 다른 위치에 이름이 같은 항목이 있습니다. " + rootName);
            Visit(source,rootName);
        }
        return items;
    }
    public static void Create(string destination, IEnumerable<string> sources, bool seven, CompressionLevel level, bool replace, IProgress<JobInfo>? progress, CancellationToken token)
    {
        var items = Inventory(sources,Path.GetFullPath(destination),token);
        if(items.Count==0) throw new IOException("압축할 파일이나 폴더를 추가하세요.");
        string temp = Path.Combine(Path.GetDirectoryName(destination)!, ".moozip-" + Guid.NewGuid().ToString("N") + ".tmp");
        long total=items.Sum(i=>i.Size), done=0;
        try
        {
            using(var disk = new FileStream(temp,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None))
            using(var zip = seven ? null : new ZipArchive(disk, ZipArchiveMode.Create, true, Encoding.UTF8))
            using(var sz = seven ? SevenZipWriter.OpenWriter(disk,new SevenZipWriterOptions()) : null)
            {
                foreach(var item in items)
                {
                    token.ThrowIfCancellationRequested();
                    void Report(long n) { done+=n; progress?.Report(new(item.Name,Math.Min(1,(double)done/Math.Max(1,total)))); }
                    Report(0);
                    if(seven && item.Folder) { sz!.WriteDirectory(item.Name,item.Modified); continue; }
                    ZipArchiveEntry? entry = zip?.CreateEntry(item.Name + (item.Folder ? "/" : ""),level);
                    if(entry!=null && item.Modified.Year is >=1980 and <=2107) entry.LastWriteTime=item.Modified;
                    if(item.Folder) continue;
                    using var input = new FileStream(item.Disk,FileMode.Open,FileAccess.Read,FileShare.Read);
                    if(input.Length!=item.Size) throw new IOException("작업 중 원본 크기가 바뀌었습니다. " + item.Name);
                    using var monitored = new ProgressStream(input,Report,token);
                    if(seven) sz!.Write(item.Name,monitored,item.Modified);
                    else { using var output=entry!.Open(); monitored.CopyTo(output); }
                }
            }
            token.ThrowIfCancellationRequested();
            File.Move(temp,destination,replace);
        }
        finally { if(File.Exists(temp)) File.Delete(temp); }
    }
    static readonly uint[] CrcTable = Enumerable.Range(0,256).Select(n => {
        uint c=(uint)n; for(int i=0;i<8;i++) c=(c&1)!=0 ? (c>>1)^0xEDB88320u : c>>1; return c;
    }).ToArray();
    static string AvailablePath(string directory,string name,bool folder)
    {
        string extension=folder ? "" : Path.GetExtension(name);
        string stem=name[..(name.Length-extension.Length)];
        for(int i=1;;i++)
        {
            string path=Path.Combine(directory,i==1 ? name : $"{stem} ({i}){extension}");
            if(!Path.Exists(path)) return path;
        }
    }
    // Decompress into a private staging directory. Publish complete top-level items
    // with atomic moves and never merge into an existing destination tree.
    public static string Process(Catalog catalog,string? destination,bool smart,IReadOnlyCollection<string>? selected,string? password,IProgress<JobInfo>? progress,CancellationToken token)
    {
        var source=new FileInfo(catalog.FileName);
        if(source.Length!=catalog.Length || source.LastWriteTimeUtc!=catalog.Stamp) throw new IOException("압축 파일이 변경되었습니다. 파일을 다시 열어 주세요.");
        HashSet<string>? keys=selected?.ToHashSet(Names);
        bool Wanted(string name)
        {
            if(keys==null || keys.Contains(name)) return true;
            for(int i=name.LastIndexOf('/');i>=0;i=name.LastIndexOf('/',i-1))
                if(keys.Contains(name[..i])) return true;
            return false;
        }
        var chosen = catalog.Members.Where(m=>Wanted(m.Path)).ToDictionary(m=>m.Path,Names);
        string? stage=null;
        long total=chosen.Values.Sum(m=>Math.Max(0,m.Size)), done=0;
        string result=destination ?? "";
        try
        {
            if(destination!=null) stage=Directory.CreateDirectory(Path.Combine(destination,".moozip-"+Guid.NewGuid().ToString("N"))).FullName;
            using var archive=ArchiveFactory.OpenArchive(catalog.FileName,Options(password));

            var buffer=new byte[128*1024];
            foreach(var (entry, open) in ReadEntries(archive))
            {
                token.ThrowIfCancellationRequested();

                if(entry.IsDirectory && (entry.Key=="." || entry.Key=="./")) continue;
                string key=SafeName(entry.Key);
                if(!chosen.Remove(key,out var member)) continue;
                if(entry.LinkTarget!=null || entry.IsDirectory!=member.Folder) throw new InvalidDataException("항목 정보가 일치하지 않습니다. " + key);
                string? target=stage==null ? null : Path.Combine(stage,key);
                if(member.Folder) { if(target!=null) System.IO.Directory.CreateDirectory(target); continue; }
                if(target!=null) System.IO.Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                using var output=target!=null ? new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None) : null;
                using var input=open();
                // WinZip AES AE-2 uses an authentication tag and stores a zero CRC.
                bool checkCrc=archive.Type==ArchiveType.Zip && !(entry.IsEncrypted && entry.Crc==0);
                long bytes=0; uint crc=uint.MaxValue; int n;
                while((n=input.Read(buffer))>0)
                {
                    token.ThrowIfCancellationRequested();
                    bytes+=n;
                    if(member.Size>=0 && bytes>member.Size) throw new InvalidDataException("항목 크기가 일치하지 않습니다. " + key);
                    output?.Write(buffer,0,n);
                    if(checkCrc) for(int i=0;i<n;i++) crc=CrcTable[(byte)(crc^buffer[i])]^(crc>>8);
                    done+=n;
                    progress?.Report(new(key,Math.Min(1,(double)done/Math.Max(1,total))));
                }
                if(bytes!=member.Size || (checkCrc && ~crc!=unchecked((uint)entry.Crc)))
                    throw new InvalidDataException("데이터 검사에 실패했습니다. " + key);
                output?.Dispose();
                if(target!=null && member.Modified is DateTime date && date.Year>=1601) File.SetLastWriteTime(target,date);
            }
            if(chosen.Count!=0) throw new InvalidDataException("일부 항목을 읽지 못했습니다.");
            token.ThrowIfCancellationRequested();
            if(stage!=null)
            {
                var top=System.IO.Directory.GetFileSystemEntries(stage);
                if(smart && top.Length>1)
                {
                    result=AvailablePath(destination!,Path.GetFileNameWithoutExtension(source.Name),true);
                    System.IO.Directory.Move(stage,result);
                    stage=null;
                }
                else
                {
                    foreach(var item in top)
                    {
                        bool folder=System.IO.Directory.Exists(item);
                        string target=AvailablePath(destination!,Path.GetFileName(item),folder);
                        if(folder) System.IO.Directory.Move(item,target); else File.Move(item,target);
                        if(top.Length==1 && folder) result=target;
                    }
                }
            }
            return result;
        }
        finally { if(stage!=null && System.IO.Directory.Exists(stage)) System.IO.Directory.Delete(stage,true); }
    }
    static IEnumerable<(IEntry Entry, Func<Stream> Open)> ReadEntries(IArchive archive)
    {
        if(archive.IsSolid || archive.Type==ArchiveType.SevenZip)
        {
            using var reader=archive.ExtractAllEntries();
            while(reader.MoveToNextEntry()) yield return (reader.Entry,()=>reader.OpenEntryStream());
        }
        else
        {
            foreach(var entry in archive.Entries) yield return (entry,entry.OpenEntryStream);
        }
    }
    sealed class ProgressStream(Stream inner, Action<long> report, CancellationToken token) : Stream
    {
        public override bool CanRead=>true;
        public override bool CanSeek=>inner.CanSeek;
        public override bool CanWrite=>false;
        public override long Length=>inner.Length;
        public override long Position { get=>inner.Position; set=>inner.Position=value; }
        public override int Read(byte[] data,int offset,int count) { token.ThrowIfCancellationRequested();int n=inner.Read(data,offset,count);report(n);return n; }
        public override long Seek(long off, SeekOrigin origin)=>inner.Seek(off,origin);
        public override void Flush() {}
        public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] data,int offset,int count)=>throw new NotSupportedException();
    }
}
