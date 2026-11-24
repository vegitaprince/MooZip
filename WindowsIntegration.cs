using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MooZip;

internal static class WindowsIntegration
{
    const string Classes = @"Software\Classes\";
    const string Capabilities = @"Software\MooZip\Capabilities";
    const string Registered = @"Software\RegisteredApplications";
    const string ClassId = "{60F872E6-1916-4F9B-8951-D20ACCDFA3C8}";
    const string CompressMenu = Classes + @"AllFilesystemObjects\shell\MooZip.Create";
    static readonly string[] Extensions = [".zip", ".7z", ".rar", ".tar"];
    static string Exe => Path.Combine(AppContext.BaseDirectory, "MooZip.exe");
    static string Icon => $"\"{Exe}\",0";
    static string ProgId(string ext) => "MooZip" + ext.ToUpperInvariant();
    static string ArchiveMenu(string ext) => Classes + @"SystemFileAssociations\" + ext + @"\shell\MooZip.Archive";

    [DllImport("shell32.dll")]
    static extern void SHChangeNotify(uint eventId, uint flags, IntPtr item1, IntPtr item2);

    static void Set(string path, string name, string value)
    {
        using var key = Registry.CurrentUser.CreateSubKey(path);
        key.SetValue(name, value);
    }
    static string? Get(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path);
        return key?.GetValue(name) as string;
    }
    static void DeleteValue(string path, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, false);
    }
    static void DeleteTree(string path) => Registry.CurrentUser.DeleteSubKeyTree(path, false);
    static void Notify() => SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero);

    static void RegisterFormats()
    {
        Set(Capabilities, "ApplicationName", "MooZip");
        Set(Capabilities, "ApplicationDescription", "ZIP, 7Z, RAR, TAR 압축 파일 관리자");
        Set(Capabilities, "ApplicationIcon", Icon);
        Set(Classes + @"Applications\MooZip.exe", "FriendlyAppName", "MooZip");
        Set(Classes + @"Applications\MooZip.exe\shell\open\command", "", $"\"{Exe}\" \"%1\"");
        foreach (string ext in Extensions)
        {
            string prog = ProgId(ext);
            string key = Classes + prog;
            Set(key, "", "MooZip " + ext[1..].ToUpperInvariant() + " 압축 파일");
            Set(key + @"\DefaultIcon", "", Icon);
            Set(key + @"\shell\open\command", "", $"\"{Exe}\" \"%1\"");
            Set(key + @"\Application", "ApplicationName", "MooZip");
            Set(key + @"\Application", "ApplicationIcon", Icon);
            Set(Capabilities + @"\FileAssociations", ext, prog);
            Set(Classes + @"Applications\MooZip.exe\SupportedTypes", ext, "");
            using var openWith = Registry.CurrentUser.CreateSubKey(Classes + ext + @"\OpenWithProgids");
            openWith.SetValue(prog, Array.Empty<byte>(), RegistryValueKind.None);
        }
        Set(Registered, "MooZip", Capabilities);
        Notify();
    }

    internal static void OpenDefaults()
    {
        // Keep the installed handler when Settings is opened from a portable copy.
        string? handler = Get(Classes + ProgId(".zip") + @"\shell\open\command", "");
        string? owner = Get(@"Software\MooZip", "AssociationOwner");
        if (owner == null || !File.Exists(owner) || handler != $"\"{owner}\" \"%1\"" || Get(Registered, "MooZip") != Capabilities)
        {
            RegisterFormats();
            Set(@"Software\MooZip", "AssociationOwner", Exe);
        }
        string uri = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)
            ? "ms-settings:defaultapps?registeredAppUser=MooZip" : "ms-settings:defaultapps";
        Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
    }

    internal static void Install(bool menus)
    {
        RegisterFormats();
        Set(@"Software\MooZip", "AssociationOwner", Exe);
        // Remove 1.0's single-selection verbs when upgrading.
        DeleteTree(Classes + @"*\shell\MooZip.Create");
        DeleteTree(Classes + @"Directory\shell\MooZip.Create");
        if (menus)
        {
            string dll = Path.Combine(AppContext.BaseDirectory, "MooZipShell.dll");
            if (!File.Exists(dll)) throw new FileNotFoundException("탐색기 메뉴 파일을 찾을 수 없습니다.", dll);
            Set(Classes + @"CLSID\" + ClassId + @"\InprocServer32", "", dll);
            Set(Classes + @"CLSID\" + ClassId + @"\InprocServer32", "ThreadingModel", "Apartment");
            Set(CompressMenu, "MUIVerb", "MooZip으로 압축…");
            Set(CompressMenu, "Icon", Icon);
            Set(CompressMenu, "MultiSelectModel", "Player");
            Set(CompressMenu, "ExplorerCommandHandler", ClassId);
            foreach (var ext in Extensions)
            {
                var menu = ArchiveMenu(ext);
                Set(menu, "MUIVerb", "MooZip");
                Set(menu, "Icon", Icon);
                Set(menu, "SubCommands", "");
                Set(menu, "MultiSelectModel", "Single");
                AddAction(menu, "01smart", "알아서 풀기", "--extract-smart ");
                AddAction(menu, "02here", "여기에 풀기", "--extract-here ");
                AddAction(menu, "03to", "폴더를 선택하여 풀기…", "--extract-to ");
                AddAction(menu, "04open", "MooZip으로 열기", "");
            }
        }
        else RemoveMenus();
        // Refresh the classic menu even if Windows rejects the optional modern package.
        Notify();
        bool sameLocation=string.Equals(Get(@"Software\MooZip","ModernMenuOwner"),Exe,StringComparison.OrdinalIgnoreCase);
        if (menus || sameLocation)
        {
            ModernMenu.Configure(menus);
            if (menus) Set(@"Software\MooZip","ModernMenuOwner",Exe);
            else DeleteValue(@"Software\MooZip","ModernMenuOwner");
        }
    }

    static void AddAction(string menu, string id, string title, string argument)
    {
        string key = menu + @"\shell\" + id;
        Set(key, "MUIVerb", title);
        Set(key + @"\command", "", $"\"{Exe}\" {argument}\"%1\"");
    }
    static void RemoveMenus()
    {
        DeleteTree(CompressMenu);
        foreach (string ext in Extensions) DeleteTree(ArchiveMenu(ext));
        DeleteTree(Classes + @"CLSID\" + ClassId);
    }
    internal static void Uninstall()
    {
        if (!string.Equals(Get(@"Software\MooZip", "AssociationOwner"), Exe, StringComparison.OrdinalIgnoreCase)) return;
        if (string.Equals(Get(@"Software\MooZip","ModernMenuOwner"),Exe,StringComparison.OrdinalIgnoreCase))
        {
            ModernMenu.Configure(false);
            DeleteValue(@"Software\MooZip","ModernMenuOwner");
        }
        RemoveMenus();
        foreach (string ext in Extensions)
        {
            DeleteValue(Classes + ext + @"\OpenWithProgids", ProgId(ext));
            DeleteTree(Classes + ProgId(ext));
        }
        DeleteTree(Classes + @"Applications\MooZip.exe");
        DeleteValue(Registered, "MooZip");
        DeleteTree(Capabilities);
        DeleteValue(@"Software\MooZip", "AssociationOwner");
        Notify();
    }
}


