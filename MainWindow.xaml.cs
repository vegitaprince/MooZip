using Microsoft.Win32;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
namespace MooZip;

public partial class MainWindow : Window
{
    Catalog? catalog;
    string? password,resultPath;
    CancellationTokenSource? job;
    bool dark;
    readonly List<string> recent=[];
    readonly string StatePath;
    static string DefaultStatePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MooZip","workspace.json");
    sealed record Preferences(bool Dark,string[] Recent);
    public MainWindow() : this(DefaultStatePath) { }
    internal MainWindow(string statePath)
    {
        StatePath=statePath;
        InitializeComponent();
        dark=Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) is 0;
        try { if(File.Exists(StatePath) && JsonSerializer.Deserialize<Preferences>(File.ReadAllText(StatePath)) is {} p) {dark=p.Dark;recent.AddRange(p.Recent.Where(File.Exists).Take(9));} } catch(IOException){} catch(JsonException){} catch(UnauthorizedAccessException){}
        ApplyTheme();DrawRecent();
        Closing+=(_,e)=>{if(job!=null){job.Cancel();e.Cancel=true;Status.Text="작업을 취소하는 중…";}};
    }
    void Save()
    {
        try {Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);string temp=StatePath+"."+Guid.NewGuid()+".tmp";File.WriteAllText(temp,JsonSerializer.Serialize(new Preferences(dark,recent.ToArray())));File.Move(temp,StatePath,true);}
        catch(IOException){} catch(UnauthorizedAccessException){}
    }
    void ApplyTheme()
    {
        string[] keys=["Bg","Panel","Ink","Muted","Edge","Surface","Action","OnAction","Rail","Selected"];
        string[] colors=dark?["#262626","#303030","#EEEEEE","#B8B8B8","#494949","#252525","#B9E2C8","#203628","#191919","#405548"]:["#E5E5E5","#F1F1F1","#242424","#606060","#D2D2D2","#FAFAFA","#294438","#FFFFFF","#282828","#D5E7DC"];
        for(int i=0;i<keys.Length;i++)Application.Current.Resources[keys[i]]=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));
    }
    void DrawRecent()
    {
        RecentList.Children.Clear();NoRecent.Visibility=recent.Count==0?Visibility.Visible:Visibility.Collapsed;
        foreach(string path in recent)
        {
            var name=new TextBlock{Text=Path.GetFileName(path),FontWeight=FontWeights.SemiBold,FontSize=13,TextTrimming=TextTrimming.CharacterEllipsis};
            var sub=new TextBlock{Text=Path.GetDirectoryName(path),Style=(Style)FindResource("Caption"),TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,3,0,0)};
            var panel=new StackPanel();panel.Children.Add(name);panel.Children.Add(sub);
            var b=new Button{Content=panel,ToolTip=path,Margin=new Thickness(0,0,0,5),Padding=new Thickness(8),HorizontalContentAlignment=HorizontalAlignment.Stretch};
            if(string.Equals(path,catalog?.FileName,StringComparison.OrdinalIgnoreCase)) b.SetResourceReference(BackgroundProperty,"Selected");
            b.Click+=async(_,_)=>await OpenAsync(path);RecentList.Children.Add(b);
        }
    }
    void Display(Catalog value)
    {
        catalog=value;Heading.Text=Path.GetFileName(value.FileName);Heading.ToolTip=value.FileName;FormatTag.Text=value.Format;
        Landing.Visibility=Visibility.Collapsed;BrowserPane.Visibility=Visibility.Visible;SearchBox.Clear();
        MemberGrid.ItemsSource=value.Members;ApplyFilter();
        recent.RemoveAll(p=>string.Equals(p,value.FileName,StringComparison.OrdinalIgnoreCase));recent.Insert(0,value.FileName);
        if(recent.Count>9)recent.RemoveRange(9,recent.Count-9);DrawRecent();Save();
        Status.Text=$"{value.Members.Length:N0}개 항목   ·   압축 파일 {ArchiveService.SizeText(value.Length)}";
    }
    void SetWorking(bool on)
    {
        CancelJob.Visibility=Meter.Visibility=on?Visibility.Visible:Visibility.Collapsed;
        BrowserPane.IsEnabled=Landing.IsEnabled=RecentList.IsEnabled=!on;
        if(on) {OpenResult.Visibility=Visibility.Collapsed;Meter.Value=0;Meter.IsIndeterminate=true;}
    }
    sealed class UiProgress(Action<JobInfo> update):IProgress<JobInfo>
    {
        readonly Stopwatch timer=Stopwatch.StartNew();
        public void Report(JobInfo info) {if(timer.ElapsedMilliseconds<100)return;timer.Restart();update(info);}
    }
    async Task RunAsync(string title,Func<CancellationToken,IProgress<JobInfo>,Task> action)
    {
        if(job!=null)return;
        using var current=new CancellationTokenSource();job=current;SetWorking(true);Status.Text=title+"…";
        var progress=new UiProgress(info=>Dispatcher.BeginInvoke(()=>{if(job!=current)return;Meter.IsIndeterminate=false;Meter.Value=info.Fraction;Status.Text=$"{title}  {info.Fraction:P0}   ·   {info.Name}";}));
        try {await action(current.Token,progress);}
        catch(OperationCanceledException){Status.Text="작업을 취소했습니다.";}
        catch(Exception e){Status.Text=title+" 실패";MessageBox.Show(this,e.Message,"MooZip · "+Status.Text,MessageBoxButton.OK,MessageBoxImage.Warning);}
        finally {job=null;SetWorking(false);}
    }
    public async Task OpenAsync(string path)
    {
        await RunAsync("파일 열기",async(token,_)=>{
            string? key=null;
            Catalog value;
            try {value=await Task.Run(()=>ArchiveService.Read(path,null,token));}
            catch(Exception e) when(e.ToString().Contains("password",StringComparison.OrdinalIgnoreCase) || e.ToString().Contains("cryptographic",StringComparison.OrdinalIgnoreCase)){
                key=CreateWindow.AskPassword(this);if(key==null)throw new OperationCanceledException();
                value=await Task.Run(()=>ArchiveService.Read(path,key,token));
            }
            password=key;Display(value);
        });
    }
    internal async Task OpenFromExplorerAsync(string path, string verb)
    {
        await OpenAsync(path);
        if (!string.Equals(catalog?.FileName, path, StringComparison.OrdinalIgnoreCase)) return;
        if (verb == "--extract-smart") await ExtractAsync(true, false, false);
        else if (verb == "--extract-here") await ExtractAsync(false, false, false, here: true);
        else if (verb == "--extract-to") await ExtractAsync(false, false, false);
    }

    void DefaultAppsClicked(object sender, RoutedEventArgs e)
    {
        try { WindowsIntegration.OpenDefaults(); }
        catch (Exception error) { MessageBox.Show(this, error.Message, "MooZip · 기본 앱 설정", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    void OpenClicked(object sender,RoutedEventArgs e)
    {
        if(job!=null)return;
        var d=new OpenFileDialog{Title="압축 파일 열기",Filter="지원 압축 파일|*.zip;*.7z;*.rar;*.tar|모든 파일|*.*"};
        if(d.ShowDialog(this)==true)_=OpenAsync(d.FileName);
    }
    internal async Task CreateAsync(string[] initial)
    {
        if(job!=null)return;
        var dialog=new CreateWindow(this,initial);
        if(dialog.ShowDialog()!=true || dialog.Request is not {} request)return;
        await RunAsync("압축하기",async(token,progress)=>{
            await Task.Run(()=>ArchiveService.Create(request.Destination,request.Sources,request.Seven,request.Level,request.Replace,progress,token));
            var value=await Task.Run(()=>ArchiveService.Read(request.Destination,null,token));
            password=null;Display(value);Result(Path.GetDirectoryName(request.Destination)!,"압축 완료   ·   "+ArchiveService.SizeText(value.Length));
        });
    }
    void Result(string path,string msg){resultPath=path;Status.Text=msg;OpenResult.Visibility=Visibility.Visible;}
    async Task ExtractAsync(bool smart,bool selected,bool test,bool here=false)
    {
        if(job!=null || catalog is not {} current)return;
        var chosen=selected ? MemberGrid.SelectedItems.Cast<Member>().Select(m=>m.Path).ToArray() : null;
        if(chosen?.Length==0)return;
        string? destination=null;
        if(!test)
        {
            destination=Path.GetDirectoryName(current.FileName)!;
            if(!smart && !here)
            {
                var d=new OpenFolderDialog{Title=selected?"선택 항목을 풀 폴더":"전체 항목을 풀 폴더",InitialDirectory=destination};
                if(d.ShowDialog(this)!=true)return;destination=d.FolderName;
            }
        }
        if(password==null && current.Members.Any(m=>m.Encrypted))
        {password=CreateWindow.AskPassword(this);if(password==null)return;}
        await RunAsync(test?"압축 파일 검사":"압축 풀기",async(token,progress)=>{
            try
            {
                string path=await Task.Run(()=>ArchiveService.Process(current,destination,smart,chosen,password,progress,token));
                if(test)Status.Text=$"검사 완료   ·   {current.Members.Count(m=>!m.Folder):N0}개 파일을 정상적으로 읽었습니다.";
                else Result(path,"압축 풀기 완료   ·   "+path);
            }
            catch { password=null;throw; }
        });
    }
    void ApplyFilter()
    {
        if(MemberGrid.ItemsSource==null)return;
        string query=SearchBox.Text.Trim();var view=CollectionViewSource.GetDefaultView(MemberGrid.ItemsSource);
        view.Filter=o=>o is Member m && m.Path.Contains(query,StringComparison.OrdinalIgnoreCase);
        NoMatches.Visibility=view.IsEmpty?Visibility.Visible:Visibility.Collapsed;SelectedButton.IsEnabled=MemberGrid.SelectedItems.Count>0;
    }
    void Home(object s,RoutedEventArgs e){if(job!=null)return;catalog=null;password=null;MemberGrid.ItemsSource=null;BrowserPane.Visibility=Visibility.Collapsed;Landing.Visibility=Visibility.Visible;Heading.Text="압축 작업 공간";Heading.ToolTip=null;FormatTag.Text="";Status.Text="준비됨";OpenResult.Visibility=Visibility.Collapsed;DrawRecent();}
    void Theme(object s,RoutedEventArgs e){dark=!dark;ApplyTheme();Save();}
    void NewClicked(object s,RoutedEventArgs e)=>_=CreateAsync([]);
    void FilterChanged(object s,TextChangedEventArgs e)=>ApplyFilter();
    void SelectionChanged(object s,SelectionChangedEventArgs e)=>SelectedButton.IsEnabled=MemberGrid.SelectedItems.Count>0;
    void SmartClicked(object s,RoutedEventArgs e)=>_=ExtractAsync(true,false,false);
    void AllClicked(object s,RoutedEventArgs e)=>_=ExtractAsync(false,false,false);
    void SelectedClicked(object s,RoutedEventArgs e)=>_=ExtractAsync(false,true,false);
    void TestClicked(object s,RoutedEventArgs e)=>_=ExtractAsync(false,false,true);
    void CancelClicked(object s,RoutedEventArgs e)=>job?.Cancel();
    void ShowResult(object s,RoutedEventArgs e){if(resultPath!=null && Directory.Exists(resultPath))Process.Start(new ProcessStartInfo(resultPath){UseShellExecute=true});}
    void DragEntered(object s,DragEventArgs e){e.Effects=job==null && e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;}
    async void Dropped(object s,DragEventArgs e)
    {
        if(job!=null || e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length==0)return;
        string[] formats=[".zip",".7z",".rar",".tar"];
        if(paths.Length==1 && File.Exists(paths[0]) && formats.Contains(Path.GetExtension(paths[0]),StringComparer.OrdinalIgnoreCase))await OpenAsync(paths[0]);else await CreateAsync(paths);
    }
    void Shortcut(object s,KeyEventArgs e)
    {
        if(Keyboard.Modifiers!=ModifierKeys.Control)return;
        if(e.Key==Key.O)OpenClicked(s,e);else if(e.Key==Key.N)NewClicked(s,e);else if(e.Key==Key.F && catalog!=null){SearchBox.Focus();SearchBox.SelectAll();}else return;e.Handled=true;
    }
    void Minimize(object s,RoutedEventArgs e)=>SystemCommands.MinimizeWindow(this);
    void Maximize(object s,RoutedEventArgs e){if(WindowState==WindowState.Maximized)SystemCommands.RestoreWindow(this);else SystemCommands.MaximizeWindow(this);}
    void CloseApp(object s,RoutedEventArgs e)=>Close();
}
