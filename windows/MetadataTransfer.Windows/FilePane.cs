using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.ComponentModel;
using MetadataTransfer.Core;

namespace MetadataTransfer.Windows;
internal sealed class FileRow : INotifyPropertyChanged
{
    public required Match Pair {get;init;}
    public required bool Originals {get;init;}
    public string? Path => Originals?Pair.Original:Pair.Export;
    public string Name => Path is null ? Originals?"No matching original":"No export" : System.IO.Path.GetFileName(Path);
    public string Detail => Path is null ? Pair.Status : System.IO.Path.GetDirectoryName(Path) ?? "";
    public bool CanSelect => Path is not null;
    public string AccessibleName => Name + ", " + Pair.Status;
    public string Indicator => Pair.Matched?"\uE73E":Path is null?"":"\uE946";
    public Brush StatusBrush => Pair.Matched?UI.Green:UI.Orange;
    private ImageSource? thumbnail;
    public ImageSource? Thumbnail {get=>thumbnail;private set{thumbnail=value;PropertyChanged?.Invoke(this,new(nameof(Thumbnail)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool loaded;
    public async void Load() { if(loaded || Path is null)return;loaded=true;Thumbnail=await Thumbnails.Load(Path,96); }
}
internal sealed class FilePane : DockPanel
{
    public readonly ListView List = new(){SelectionMode=SelectionMode.Extended,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Padding=new Thickness(0)};
    private readonly TextBlock path=UI.Muted("Drop files or folders here");
    private readonly TextBlock count=UI.Muted("");
    private readonly TextBlock emptyTitle=UI.Text("Drop files or folders here",13,true);
    private readonly StackPanel empty=new(){HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
    private readonly Button folderButton;
    public bool Originals {get;}
    public event Action<string[],bool>? FilesDropped;
    public event Action? ChooseRequested;
    public event Action? DefaultRequested;
    public event Action? ClearDefaultRequested;
    public event Action? ClearRequested;
    public event Action? SelectionChanged;
    public event Action? PreviewRequested;
    public event Action? WorkflowRequested;
    public string? Folder {get;set;}
    public FilePane(bool originals)
    {
        Originals=originals;
        emptyTitle.Text=originals?"Drop original files here":"Drop exported files here";
        List.Visibility=Visibility.Collapsed;
        var header=new DockPanel{Margin=new Thickness(16,10,12,10)};
        folderButton = UI.Button("\uE8B7", OpenOrChooseFolder);
        folderButton.FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        folderButton.Padding = new Thickness(8, 4, 8, 4);
        folderButton.Margin = new Thickness(0, 0, 8, 0);
        DockPanel.SetDock(folderButton, Dock.Left);
        header.Children.Add(folderButton);
        UpdateFolderButton();
        var menu=UI.Button("⋯",()=>{},"Folder options");var context=new ContextMenu();menu.ContextMenu=context;
        MenuItem Item(string title,Action action) { var m=new MenuItem{Header=title};m.Click+=(_,_)=>action();context.Items.Add(m);return m; }
        Item("Choose folder…",()=>ChooseRequested?.Invoke());
        var open = Item("Open in Explorer", OpenFolder);
        var copyPath = Item("Copy folder path", () => { if (Folder is string folder) Clipboard.SetText(folder); });
        context.Opened += (_, _) => open.IsEnabled = copyPath.IsEnabled = Folder is not null;
        if(!originals) { Item("Save as default exports folder",()=>DefaultRequested?.Invoke());Item("Clear default exports folder",()=>ClearDefaultRequested?.Invoke()); }
        Item("Clear pane",()=>ClearRequested?.Invoke());
        menu.Click+=(_,_)=>{context.PlacementTarget=menu;context.IsOpen=true;};DockPanel.SetDock(menu,Dock.Right);header.Children.Add(menu);
        var labels=new StackPanel();labels.Children.Add(UI.Text(originals?"Originals":"Exports",14,true));path.TextTrimming=TextTrimming.CharacterEllipsis;path.TextWrapping=TextWrapping.NoWrap;labels.Children.Add(path);header.Children.Add(labels);DockPanel.SetDock(header,Dock.Top);Children.Add(header);
        count.Margin=new Thickness(16,6,16,8);DockPanel.SetDock(count,Dock.Bottom);Children.Add(count);
        var content=new Grid{Background=UI.Stripes()};
        content.Children.Add(List);
        var emptyIcon=UI.Glyph(originals?"\uE838":"\uE8DE");
        emptyIcon.FontSize=24;
        emptyIcon.Margin=new Thickness(0,0,0,10);
        empty.Children.Add(emptyIcon);
        emptyTitle.TextAlignment=TextAlignment.Center;
        empty.Children.Add(emptyTitle);
        var choose=UI.Button("Choose folder…",()=>ChooseRequested?.Invoke());
        choose.HorizontalAlignment=HorizontalAlignment.Center;
        choose.Margin=new Thickness(0,9,0,0);
        empty.Children.Add(choose);
        content.Children.Add(empty);
        var dropOutline=new Border
        {
            BorderThickness=new Thickness(2),
            CornerRadius=new CornerRadius(6),
            Margin=new Thickness(2),
            Visibility=Visibility.Collapsed,
            IsHitTestVisible=false
        };
        dropOutline.SetResourceReference(Border.BorderBrushProperty,SystemColors.HighlightBrushKey);
        content.Children.Add(dropOutline);
        Children.Add(content);
        var style=new Style(typeof(ListViewItem),(Style?)Application.Current.TryFindResource(typeof(ListViewItem)));style.Setters.Add(new Setter(System.Windows.Automation.AutomationProperties.NameProperty,new Binding("AccessibleName")));style.Setters.Add(new Setter(HeightProperty,44d));style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Stretch));style.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(12,3,12,3)));style.Setters.Add(new Setter(Control.MarginProperty,new Thickness(4,0,4,0)));style.Setters.Add(new Setter(ToolTipProperty,new Binding("Path")));style.Setters.Add(new Setter(IsEnabledProperty, new Binding(nameof(FileRow.CanSelect))));List.ItemContainerStyle=style;
        var root=new FrameworkElementFactory(typeof(DockPanel));
        root.AddHandler(LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is FrameworkElement { DataContext: FileRow row })row.Load();}));
        var dot=new FrameworkElementFactory(typeof(TextBlock));dot.SetBinding(TextBlock.TextProperty,new Binding("Indicator"));dot.SetBinding(TextBlock.ForegroundProperty,new Binding("StatusBrush"));dot.SetValue(TextBlock.FontFamilyProperty,UI.IconFont);dot.SetValue(DockPanel.DockProperty,Dock.Right);dot.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);dot.SetValue(MarginProperty,new Thickness(8,0,0,0));root.AppendChild(dot);
        var image=new FrameworkElementFactory(typeof(Image));image.SetValue(WidthProperty,54d);image.SetValue(HeightProperty,34d);image.SetValue(MarginProperty,new Thickness(0,0,10,0));image.SetBinding(Image.SourceProperty,new Binding("Thumbnail"));root.AppendChild(image);
        var texts=new FrameworkElementFactory(typeof(StackPanel));var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new Binding("Name"));title.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);texts.AppendChild(title);
        var detail=new FrameworkElementFactory(typeof(TextBlock));detail.SetBinding(TextBlock.TextProperty,new Binding("Detail"));detail.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);detail.SetValue(TextBlock.FontSizeProperty,10d);detail.SetValue(OpacityProperty,.6);texts.AppendChild(detail);root.AppendChild(texts);List.ItemTemplate=new DataTemplate{VisualTree=root};
        VirtualizingPanel.SetIsVirtualizing(List,true);VirtualizingPanel.SetVirtualizationMode(List,VirtualizationMode.Recycling);
        List.SelectionChanged+=(_,_)=>SelectionChanged?.Invoke();
        List.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space){e.Handled=true;PreviewRequested?.Invoke();} if(e.Key==Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)){List.SelectAll();e.Handled=true;}};
        List.MouseDoubleClick+=(_,e)=>
        {
            if(e.OriginalSource is DependencyObject target&&ItemsControl.ContainerFromElement(List,target) is ListViewItem { IsEnabled:true })
                PreviewRequested?.Invoke();
        };
        List.PreviewMouseRightButtonDown+=(_,e)=>
        {
            DependencyObject? target=e.OriginalSource as DependencyObject;
            while(target is not null && target is not ListViewItem) target=VisualTreeHelper.GetParent(target);
            if (target is ListViewItem { IsEnabled: false })
            {
                e.Handled = true;
                return;
            }
            if(target is ListViewItem item && !item.IsSelected){List.SelectedItems.Clear();item.IsSelected=true;}
        };
        var fileMenu=new ContextMenu();
        void FileAction(string title,Action action) {var m=new MenuItem{Header=title};m.Click+=(_,_)=>action();fileMenu.Items.Add(m);}
        FileAction("Quick preview    Space",()=>PreviewRequested?.Invoke());
        FileAction("Open",()=>{foreach(var p in SelectedPaths)UI.Open(p);});
        FileAction("Show in Explorer",()=>{if(SelectedPaths.FirstOrDefault() is string p)UI.Reveal(p);});
        FileAction("Copy paths",()=>{if(SelectedPaths.Length>0)Clipboard.SetText(string.Join(Environment.NewLine,SelectedPaths));});
        FileAction("Apply workflow…",()=>WorkflowRequested?.Invoke());List.ContextMenu=fileMenu;
        AllowDrop=true;
        DragOver+=(_,e)=>
        {
            bool supported=e.Data.GetDataPresent(DataFormats.FileDrop);
            e.Effects=supported?DragDropEffects.Copy:DragDropEffects.None;
            dropOutline.Visibility=supported?Visibility.Visible:Visibility.Collapsed;
            e.Handled=true;
        };
        DragLeave+=(_,_)=>dropOutline.Visibility=Visibility.Collapsed;
        Drop+=(_,e)=>
        {
            dropOutline.Visibility=Visibility.Collapsed;
            if(e.Data.GetData(DataFormats.FileDrop) is string[] paths)FilesDropped?.Invoke(paths,Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            e.Handled=true;
        };
    }
    public string[] SelectedPaths => List.SelectedItems.Cast<FileRow>().Select(r=>r.Path).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    private void OpenOrChooseFolder()
    {
        if (Folder is null) ChooseRequested?.Invoke();
        else OpenFolder();
    }

    public void OpenFolder()
    {
        if (Folder is null) return;
        if (!System.IO.Directory.Exists(Folder))
        {
            MessageBox.Show(Window.GetWindow(this), "Reconnect the drive or choose another folder.", "Folder unavailable");
            return;
        }
        UI.Open(Folder);
    }

    private void UpdateFolderButton()
    {
        var pane = Originals ? "originals" : "exports";
        var label = Folder is null ? $"Choose {pane} folder" : $"Open {pane} in Explorer";
        folderButton.ToolTip = Folder is null ? label : $"{label}\n{Folder}";
        System.Windows.Automation.AutomationProperties.SetName(folderButton, label);
    }

    public void SetRows(IEnumerable<Match> matches,string? folder,int fileCount)
    {
        Folder=folder;path.Text=folder??(fileCount>0?$"{fileCount} individual files":"Drop files or folders here");path.ToolTip=path.Text;
        UpdateFolderButton();
        var rows=matches.Select(m=>new FileRow{Pair=m,Originals=Originals}).ToArray();List.ItemsSource=rows;
        emptyTitle.Text = folder is null ? Originals?"Drop original files here":"Drop exported files here" : "No supported media in this folder";
        List.Visibility=fileCount==0?Visibility.Collapsed:Visibility.Visible;
        empty.Visibility=fileCount==0?Visibility.Visible:Visibility.Collapsed;
        count.Text=$"{fileCount} {(fileCount == 1 ? "file" : "files")}";
        // Thumbnails load only when virtualized row containers become visible.
    }
}
