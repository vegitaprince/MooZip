using System.Windows;
using System.Text;
using System.IO;
namespace MooZip;
public partial class App : Application
{
    FileStream? updateLease;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        if (e.Args is ["--package-portable", string output])
        {
            RunMaintenance(() =>
            {
                PortablePackage.Create(UpdateHandoff.AppDirectory, Path.GetFullPath(output));
                return 0;
            });
            return;
        }
        if (e.Args is ["--apply-portable", string target, string version, string action, string process, string started])
        {
            RunMaintenance(() => UpdateHandoff.Apply(target, version, action == "restart", int.Parse(process), long.Parse(started)), action == "restart");
            return;
        }
        try { updateLease = UpdateHandoff.EnterApp(); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show("MooZip을 시작하지 못했습니다. 업데이트가 진행 중이라면 잠시 후 다시 실행하세요.\n\n" + error.Message,
                "MooZip", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
            return;
        }
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
        window.StartUpdates();
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

    void RunMaintenance(Func<int> action, bool showError = false)
    {
        try { Shutdown(action()); }
        catch (Exception error)
        {
            if (showError) MessageBox.Show(error.Message, "MooZip · 업데이트 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
            Shutdown(1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        (MainWindow as MainWindow)?.EndSession();
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        updateLease?.Dispose();
        base.OnExit(e);
    }
}
