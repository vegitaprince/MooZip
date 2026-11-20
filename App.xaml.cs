using System.Windows;
using System.Text;
using System.IO;
namespace MooZip;
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (e.Args is [string mode] && mode is "--install-menus" or "--install-formats" or "--unregister" or "--defaults")
        {
            try
            {
                if (mode == "--defaults") WindowsIntegration.OpenDefaults();
                else if (mode == "--unregister") WindowsIntegration.Uninstall();
                else WindowsIntegration.Install(mode == "--install-menus");
                Shutdown(0);
            }
            catch (Exception error)
            {
                if (mode == "--defaults") MessageBox.Show(error.Message, "MooZip · 기본 앱 설정");
                Shutdown(1);
            }
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
        if(e.Args.Length>1 && e.Args[0]=="--create")
        {
            var inputs=e.Args.Skip(1).Where(p=>File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath).ToArray();
            if(inputs.Length>0) window.Dispatcher.InvokeAsync(()=>window.CreateAsync(inputs));
        }
        else if (e.Args is [string verb, string archive] && verb is "--extract-smart" or "--extract-here" or "--extract-to")
            window.Dispatcher.InvokeAsync(() => window.OpenFromExplorerAsync(Path.GetFullPath(archive), verb));
        else if(e.Args.FirstOrDefault() is string path && File.Exists(path))
            window.Dispatcher.InvokeAsync(() => window.OpenAsync(Path.GetFullPath(path)));
    }
}
