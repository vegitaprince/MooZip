using System.Windows;
using System.Windows.Controls;

namespace MooZip;

internal sealed class UpdateWindow : Window
{
    internal readonly Button Check = new() { Content = "지금 확인" };
    internal readonly Button Apply = new() { Content = "다운로드 및 다시 시작" };
    internal readonly CheckBox Automatic = new() { Content = "자동 다운로드 후 MooZip을 닫을 때 적용" };
    internal readonly TextBlock Message = new() { TextWrapping = TextWrapping.Wrap };

    internal UpdateWindow(Window owner, bool automatic)
    {
        SetResourceReference(StyleProperty, typeof(Window));
        Owner = owner;
        Title = "MooZip 업데이트";
        Width = 475;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;

        var panel = new StackPanel { Margin = new Thickness(24) };
        Content = panel;
        panel.Children.Add(new TextBlock
        {
            Text = $"MooZip {UpdateClient.CurrentVersion}",
            FontSize = 22,
            FontWeight = FontWeights.SemiBold
        });
        Message.Margin = new Thickness(0, 12, 0, 16);
        panel.Children.Add(Message);
        Automatic.IsChecked = automatic;
        Automatic.Margin = new Thickness(0, 0, 0, 20);
        Automatic.SetResourceReference(ForegroundProperty, "Ink");
        panel.Children.Add(Automatic);
        var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        Check.Margin = new Thickness(0, 0, 8, 0);
        Apply.Style = (Style)FindResource("Primary");
        buttons.Children.Add(Check);
        buttons.Children.Add(Apply);
        panel.Children.Add(buttons);
    }
}
