using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Diagnostics;
namespace MetadataTransfer.Windows;

internal static class UI
{
    public static readonly Brush Green = new SolidColorBrush(Color.FromRgb(51,160,107));
    public static readonly Brush Orange = new SolidColorBrush(Color.FromRgb(209,133,38));
    public static Brush Subtle => new SolidColorBrush(Color.FromArgb(14,128,128,128));
    public static TextBlock Text(string text, double size=12, bool bold=false) => new() { Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center };
    public static TextBlock Muted(string text,double size=11) { var t=Text(text,size);t.Opacity=.65;return t; }
    public static void SetStatusText(TextBlock label, string text, Brush? marker)
    {
        label.Inlines.Clear();
        if (marker is not null)
        {
            label.Inlines.Add(new Run("●") { Foreground = marker, FontSize = 8 });
            label.Inlines.Add(new Run("  "));
        }
        label.Inlines.Add(new Run(text));
        AutomationProperties.SetName(label, text);
    }
    public static Button Button(string text, Action action,string? tip=null)
    {
        var b=new Button {Content=text,ToolTip=tip??text,Padding=new Thickness(12,6,12,6),MinHeight=32,Margin=new Thickness(3)}; b.Click+=(_,_)=>action();AutomationProperties.SetName(b,tip??text);return b;
    }
    public static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    public static TextBlock Glyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = IconFont,
        FontSize = 16,
        VerticalAlignment = VerticalAlignment.Center,
        HorizontalAlignment = HorizontalAlignment.Center
    };

    public static Button IconButton(string glyph, string label, Action? action = null, string shortcut = "")
    {
        var button = new Button();
        StyleIconButton(button, glyph, label, shortcut);
        if (action is not null) button.Click += (_, _) => action();
        return button;
    }

    public static void StyleIconButton(ButtonBase button, string glyph, string label, string shortcut = "")
    {
        button.Content = Glyph(glyph);
        button.Width = 34;
        button.Height = 32;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(2);
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.ToolTip = shortcut.Length == 0 ? label : $"{label} ({shortcut})";
        ToolTipService.SetShowOnDisabled(button, true);
        AutomationProperties.SetName(button, label);
    }

    public static TextBox Input(string label,string value="",bool multiline=false)
    {
        var t=new TextBox { MinHeight=32,Padding=new Thickness(8,5,8,5),VerticalContentAlignment=VerticalAlignment.Center,Text=value,ToolTip=label,AcceptsReturn=multiline,TextWrapping=multiline?TextWrapping.Wrap:TextWrapping.NoWrap,VerticalScrollBarVisibility=multiline?ScrollBarVisibility.Auto:ScrollBarVisibility.Hidden };
        AutomationProperties.SetName(t,label);return t;
    }
    public static StackPanel Horizontal(params UIElement[] children) { var p=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};foreach(var c in children)p.Children.Add(c);return p; }
    public static Border Card(UIElement child,Thickness? padding=null) => new() {Child=child,Padding=padding??new Thickness(12),Background=Subtle,CornerRadius=new CornerRadius(8)};
    public static void Open(string target) { try { Process.Start(new ProcessStartInfo(target){UseShellExecute=true}); } catch(Exception e) { MessageBox.Show(e.Message,"Could not open item"); } }
    public static void Reveal(string path) { var p=new ProcessStartInfo("explorer.exe"){UseShellExecute=false};p.ArgumentList.Add("/select,");p.ArgumentList.Add(path);Process.Start(p); }
    public static Window Window(string title,double width,double height,Window? owner=null) => new() {Title=title,Width=width,Height=height,MinWidth=Math.Min(width,600),MinHeight=Math.Min(height,400),Owner=owner,WindowStartupLocation=owner is null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner,ThemeMode=ThemeMode.System,FontFamily=new FontFamily("Segoe UI Variable Text")};
    public static void Results(Window owner,string title,string content)
    {
        var w=Window(title,840,580,owner);var grid=new DockPanel{Margin=new Thickness(20)};
        var footer=Horizontal(Button("Copy results",()=>Clipboard.SetText(content)),Button("Done",()=>w.Close()));DockPanel.SetDock(footer,Dock.Bottom);grid.Children.Add(footer);
        var heading=Text(title,20,true);heading.Margin=new Thickness(0,0,0,16);DockPanel.SetDock(heading,Dock.Top);grid.Children.Add(heading);
        var box=Input("Operation details",content,true);box.IsReadOnly=true;box.FontFamily=new FontFamily("Cascadia Mono, Consolas");box.FontSize=12;grid.Children.Add(box);w.Content=grid;w.ShowDialog();
    }
    public static DrawingBrush Stripes()
    {
        var group=new DrawingGroup();group.Children.Add(new GeometryDrawing(Brushes.Transparent,null,new RectangleGeometry(new Rect(0,0,1,88))));
        group.Children.Add(new GeometryDrawing(Subtle,null,new RectangleGeometry(new Rect(0,0,1,44))));
        return new DrawingBrush(group){TileMode=TileMode.Tile,Viewport=new Rect(0,0,1,88),ViewportUnits=BrushMappingMode.Absolute,Stretch=Stretch.Fill};
    }
}
