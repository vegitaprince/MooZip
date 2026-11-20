using Microsoft.Win32;
using System.IO;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Collections.ObjectModel;
namespace MooZip;

public sealed record CreateRequest(string[] Sources,string Destination,bool Seven,CompressionLevel Level,bool Replace);
internal sealed class CreateWindow : Window
{
    readonly ObservableCollection<string> sources=[];
    readonly ListBox list=new() {SelectionMode=SelectionMode.Extended};
    readonly ComboBox format=new(), level=new();
    readonly Button save=new(), remove=new();
    public CreateRequest? Request {get; private set;}
    public CreateWindow(Window owner,IEnumerable<string> initial)
    {
        SetResourceReference(StyleProperty,typeof(Window));
        Owner=owner; Title="새로 압축 · MooZip"; Width=580;Height=475;MinWidth=440;MinHeight=390;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        AllowDrop=true;
        Drop+=(_,e)=>{if(e.Data.GetData(DataFormats.FileDrop) is string[] paths) Add(paths);e.Handled=true;};
        var panel=new DockPanel{Margin=new Thickness(22)};
        Content=panel;
        var heading=new TextBlock{Text="새로 압축",FontSize=23,FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,0,0,15)};
        DockPanel.SetDock(heading,Dock.Top);panel.Children.Add(heading);
        var tools=new WrapPanel{Margin=new Thickness(0,0,0,8)};DockPanel.SetDock(tools,Dock.Top);panel.Children.Add(tools);
        Button Action(string text,Action callback) {var b=new Button{Content=text,Margin=new Thickness(0,0,6,0)};b.Click+=(_,_)=>callback();return b;}
        tools.Children.Add(Action("파일 추가",()=>{var d=new OpenFileDialog{Multiselect=true,Title="압축할 파일 추가"}; if(d.ShowDialog(this)==true)Add(d.FileNames);}));
        tools.Children.Add(Action("폴더 추가",()=>{var d=new OpenFolderDialog{Multiselect=true,Title="압축할 폴더 추가"};if(d.ShowDialog(this)==true) Add(d.FolderNames);}));
        remove.Content="선택 제외";remove.IsEnabled=false;remove.Click+=(_,_)=>{foreach(var path in list.SelectedItems.Cast<string>().ToArray()) sources.Remove(path);Refresh();};tools.Children.Add(remove);
        var footer=new StackPanel();DockPanel.SetDock(footer,Dock.Bottom);panel.Children.Add(footer);
        var opts=new WrapPanel{Margin=new Thickness(0,14,0,14)};
        void Field(string label,ComboBox field,string[] values)
        {
            opts.Children.Add(new TextBlock{Text=label,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,8,0)});
            field.ItemsSource=values;field.SelectedIndex=0;field.MinWidth=86;field.Height=31;field.Margin=new Thickness(0,0,16,0);opts.Children.Add(field);
            System.Windows.Automation.AutomationProperties.SetName(field,label);
        }
        Field("형식",format,["ZIP","7Z"]);Field("압축 수준",level,["표준","빠르게","최대","저장만"]);
        format.SelectionChanged+=(_,_)=>{level.IsEnabled=format.SelectedIndex==0;if(!level.IsEnabled) level.SelectedIndex=0;};footer.Children.Add(opts);
        var buttons=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};
        var cancel=Action("취소",Close);cancel.IsCancel=true;buttons.Children.Add(cancel);
        save.Content="저장 위치 선택…";save.IsDefault=true;save.Style=(Style)FindResource("Primary");save.Click+=(_,_)=>Choose();buttons.Children.Add(save);footer.Children.Add(buttons);
        list.ItemsSource=sources;list.SelectionChanged+=(_,_)=>remove.IsEnabled=list.SelectedItems.Count>0;
        System.Windows.Automation.AutomationProperties.SetName(list,"압축할 파일과 폴더");panel.Children.Add(list);Add(initial);
    }
    void Refresh()=>save.IsEnabled=sources.Count>0;
    void Add(IEnumerable<string> paths)
    {
        foreach(var p in paths) if(!sources.Contains(p,StringComparer.OrdinalIgnoreCase))sources.Add(p);
        Refresh();
    }
    void Choose()
    {
        bool seven=format.SelectedIndex==1;
        string ext=seven?"7z":"zip";
        string stem=Directory.Exists(sources[0])?Path.GetFileName(sources[0]):Path.GetFileNameWithoutExtension(sources[0]);
        var d=new SaveFileDialog{Title="압축 파일 저장",FileName=stem+"."+ext,DefaultExt="."+ext,Filter=ext.ToUpperInvariant()+" 압축 파일|*."+ext,InitialDirectory=Path.GetDirectoryName(sources[0]),OverwritePrompt=true};
        if(d.ShowDialog(this)!=true)return;
        if(!string.Equals(Path.GetExtension(d.FileName),"."+ext,StringComparison.OrdinalIgnoreCase)) {MessageBox.Show(this,"파일 확장자를 ."+ext+"로 지정해 주세요.",Title);return;}
        CompressionLevel[] levels=[CompressionLevel.Optimal,CompressionLevel.Fastest,CompressionLevel.SmallestSize,CompressionLevel.NoCompression];
        Request=new(sources.ToArray(),d.FileName,seven,levels[level.SelectedIndex],File.Exists(d.FileName));DialogResult=true;
    }
    public static string? AskPassword(Window owner)
    {
        var w=new Window{Title="암호 입력 · MooZip",Owner=owner,Width=375,SizeToContent=SizeToContent.Height,ResizeMode=ResizeMode.NoResize,WindowStartupLocation=WindowStartupLocation.CenterOwner};
        var panel=new StackPanel{Margin=new Thickness(22)};w.Content=panel;
        panel.Children.Add(new TextBlock{Text="압축 파일 암호",FontSize=18,FontWeight=FontWeights.SemiBold});
        var box=new PasswordBox{Padding=new Thickness(6),Margin=new Thickness(0,12,0,14)};panel.Children.Add(box);
        var row=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right};panel.Children.Add(row);
        var cancel=new Button{Content="취소",IsCancel=true,Margin=new Thickness(0,0,6,0)};row.Children.Add(cancel);
        var ok=new Button{Content="확인",IsDefault=true,Style=(Style)w.FindResource("Primary")};ok.Click+=(_,_)=>w.DialogResult=true;row.Children.Add(ok);w.ContentRendered+=(_,_)=>box.Focus();
        return w.ShowDialog()==true ? box.Password : null;
    }
}
