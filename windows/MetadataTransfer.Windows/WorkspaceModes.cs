using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MetadataTransfer.Core;
using Microsoft.Win32;

namespace MetadataTransfer.Windows;

public sealed partial class MainWindow
{
    private enum WorkspaceMode { Preview, Metadata, Map, Transfer }
    private WorkspaceMode mode = WorkspaceMode.Preview;
    private readonly Grid workspaces = new();
    private readonly Grid mediaWorkspace = new();
    private readonly ListBox modePicker = new()
    {
        VerticalAlignment=VerticalAlignment.Center, BorderThickness=new Thickness(0),
        Background=Brushes.Transparent, Margin=new Thickness(8,0,8,0),
        ItemsPanel=new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)))
    };
    private readonly ObservableCollection<WorkspaceMedia> mediaItems = [];
    private readonly HashSet<string> mediaSelection = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,GeoPoint> locationDrafts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,List<string>> workflowDrafts = new(StringComparer.OrdinalIgnoreCase);
    private readonly DataGrid mediaList = new()
    {
        AutoGenerateColumns=false, IsReadOnly=true, SelectionMode=DataGridSelectionMode.Extended,
        SelectionUnit=DataGridSelectionUnit.FullRow, RowHeight=42, GridLinesVisibility=DataGridGridLinesVisibility.None,
        HeadersVisibility=DataGridHeadersVisibility.Column, Background=Brushes.Transparent, BorderThickness=new Thickness(0),
        RowBackground=Brushes.Transparent, Margin=new Thickness(10,5,10,6), EnableRowVirtualization=true
    };
    private readonly StackPanel mediaEmpty = new() { HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
    private Button addFiles = null!, saveAll = null!;
    private string[] mediaInputs = [];
    private MapPane? mapPane;
    private bool syncingMedia;
    private string[] PendingPaths => drafts.Where(pair=>pair.Value.Count>0).Select(pair=>pair.Key)
        .Concat(locationDrafts.Keys).Concat(workflowDrafts.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private void InitializeModes()
    {
        var horizontal = new FrameworkElementFactory(typeof(StackPanel));
        horizontal.SetValue(StackPanel.OrientationProperty,Orientation.Horizontal);
        modePicker.ItemsPanel=new ItemsPanelTemplate(horizontal);
        var tab = new Style(typeof(ListBoxItem),(Style)FindResource("WorkspaceListItemStyle"));
        tab.Setters.Add(new Setter(MarginProperty,new Thickness(1,0,1,0)));
        tab.Setters.Add(new Setter(Control.PaddingProperty,new Thickness(9,3,9,3)));
        tab.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty,HorizontalAlignment.Center));
        tab.Setters.Add(new Setter(MinWidthProperty,62d));
        modePicker.ItemContainerStyle=tab;
        modePicker.ItemsSource=Enum.GetValues<WorkspaceMode>();
        modePicker.SelectedItem=mode;
        System.Windows.Automation.AutomationProperties.SetName(modePicker,"Workspace mode");
        modePicker.SelectionChanged+=(_,_)=> { if(modePicker.SelectedItem is WorkspaceMode value && value!=mode)SetMode(value); };
        mediaWorkspace.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(340),MinWidth=235});
        mediaWorkspace.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
        mediaList.ItemsSource=mediaItems;
        mediaList.AlternatingRowBackground = (Brush)FindResource("WorkspaceStripeBrush");
        mediaList.CellStyle = (Style)FindResource("WorkspaceGridCellStyle");
        var rowStyle = new Style(typeof(DataGridRow),(Style)FindResource("WorkspaceGridRowStyle"));
        rowStyle.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,new Binding(nameof(WorkspaceMedia.Path))));
        mediaList.RowStyle=rowStyle;
        var thumbnail=new FrameworkElementFactory(typeof(Image));
        thumbnail.SetBinding(Image.SourceProperty,new Binding(nameof(WorkspaceMedia.Thumbnail)));
        thumbnail.SetValue(WidthProperty,48d);
        thumbnail.SetValue(HeightProperty,31d);
        thumbnail.AddHandler(LoadedEvent,new RoutedEventHandler((sender,_)=>{if(sender is FrameworkElement {DataContext:WorkspaceMedia item})item.LoadThumbnail();}));
        mediaList.Columns.Add(new DataGridTemplateColumn{Header="",Width=new DataGridLength(60),CanUserSort=false,CellTemplate=new DataTemplate{VisualTree=thumbnail}});
        AddColumn("File",nameof(WorkspaceMedia.Name),new DataGridLength(2,DataGridLengthUnitType.Star),185);
        AddColumn("Tags",nameof(WorkspaceMedia.TagCount),new DataGridLength(55),48);
        AddColumn("Camera make",nameof(WorkspaceMedia.Make),new DataGridLength(130),85);
        AddColumn("Capture date",nameof(WorkspaceMedia.CaptureDate),new DataGridLength(170),110);
        AddColumn("GPS coordinates",nameof(WorkspaceMedia.Coordinates),new DataGridLength(175),150,
            nameof(WorkspaceMedia.GpsOrder));
        AddColumn("Status",nameof(WorkspaceMedia.Status),new DataGridLength(75),64);
        mediaList.SelectionChanged+=(_,_)=>
        {
            if(syncingMedia||mode==WorkspaceMode.Transfer)return;
            mediaSelection.Clear();
            mediaSelection.UnionWith(mediaList.SelectedItems.Cast<WorkspaceMedia>().Select(item=>item.Path));
            UpdateControls();UpdateMap(true);Inspect();
        };
        mediaList.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Space){Preview();e.Handled=true;}};
        mediaList.MouseDoubleClick+=(_,e)=>{if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(mediaList,source) is DataGridRow)Preview();};
        mediaList.PreviewMouseRightButtonDown+=(_,e)=>
        {
            if(e.OriginalSource is DependencyObject source&&ItemsControl.ContainerFromElement(mediaList,source) is DataGridRow { IsSelected:false } row)
                mediaList.SelectedItem=row.Item;
        };
        var menu=new ContextMenu();
        var quick=new MenuItem{Header="Quick preview",InputGestureText="Space"};
        quick.Click+=(_,_)=>Preview();
        var reveal=new MenuItem{Header="Show in Explorer"};
        reveal.Click+=(_,_)=>{if(SelectedPaths.FirstOrDefault() is string path)UI.Reveal(path);};
        var workflow=new MenuItem{Header="Stage workflow…"};
        workflow.Click+=(_,_)=>OpenWorkflows();
        menu.Items.Add(quick);menu.Items.Add(reveal);menu.Items.Add(workflow);
        menu.Opened+=(_,_)=>quick.IsEnabled=reveal.IsEnabled=workflow.IsEnabled=!busy&&mediaSelection.Count>0;
        mediaList.ContextMenu=menu;
        mediaWorkspace.Children.Add(mediaList);
        Grid.SetColumnSpan(mediaEmpty,2);
        mediaEmpty.Children.Add(UI.Text("Drop photos, videos, or folders here",16,true));
        var actions=UI.Horizontal(UI.Button("Add files…",ChooseMedia),UI.Button("Add folder…",ChooseMediaFolder));
        actions.HorizontalAlignment=HorizontalAlignment.Center;
        actions.Margin=new Thickness(0,10,0,0);
        mediaEmpty.Children.Add(actions);
        mediaWorkspace.Children.Add(mediaEmpty);
        mediaWorkspace.Background=Brushes.Transparent;
        mediaWorkspace.AllowDrop=true;
        mediaWorkspace.PreviewDragOver+=(_,e)=>{e.Effects=!busy&&e.Data.GetDataPresent(DataFormats.FileDrop)?DragDropEffects.Copy:DragDropEffects.None;e.Handled=true;};
        mediaWorkspace.PreviewDrop+=(_,e)=>
        {
            if(e.Data.GetData(DataFormats.FileDrop) is string[] paths) _=AddMedia(paths);
            e.Handled=true;
        };
    }

    private void AddColumn(string header,string property,DataGridLength width,double minimum,string? sortProperty=null)
    {
        mediaList.Columns.Add(new DataGridTextColumn
        {
            Header=header,Binding=new Binding(property),SortMemberPath=sortProperty??property,Width=width,MinWidth=minimum,
            ElementStyle=new Style(typeof(TextBlock)){Setters={new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis),new Setter(VerticalAlignmentProperty,VerticalAlignment.Center),new Setter(MarginProperty,new Thickness(4,0,8,0))}}
        });
    }

    private void SetMode(WorkspaceMode value)
    {
        if(busy)return;
        mode=value;
        modePicker.SelectedItem=value;
        bool transfer=value==WorkspaceMode.Transfer;
        workspace.Visibility=transfer?Visibility.Visible:Visibility.Collapsed;
        mediaWorkspace.Visibility=transfer?Visibility.Collapsed:Visibility.Visible;
        addFiles.Visibility=transfer?Visibility.Collapsed:Visibility.Visible;
        for(var i=1;i<folderCommands.Children.Count;i++)folderCommands.Children[i].Visibility=transfer?Visibility.Visible:Visibility.Collapsed;
        copy.Visibility=transfer?Visibility.Visible:Visibility.Collapsed;
        previewPaneButton.Visibility=transfer?Visibility.Visible:Visibility.Collapsed;
        workspace.Children.Remove(inspector);mediaWorkspace.Children.Remove(inspector);
        if(transfer)
        {
            Grid.SetColumn(inspector,4);workspace.Children.Add(inspector);
            inspector.Visibility=settings.ShowInspector?Visibility.Visible:Visibility.Collapsed;
            inspectorColumn.MinWidth=settings.ShowInspector?290:0;
            inspectorColumn.Width=new GridLength(settings.ShowInspector?inspectorWidth:0);
            inspectorDivider.Width=new GridLength(settings.ShowInspector?5:0);
            UpdatePreviewButton(settings.ShowInspector);
        }
        else if(value==WorkspaceMode.Metadata)
        {
            Grid.SetColumn(inspector,1);mediaWorkspace.Children.Add(inspector);inspector.Visibility=Visibility.Visible;
        }
        inspector.SetWorkspaceEditing(value==WorkspaceMode.Metadata);
        Grid.SetColumnSpan(mediaList,value==WorkspaceMode.Preview?2:1);
        for(var i=2;i<mediaList.Columns.Count;i++)mediaList.Columns[i].Visibility=
            value==WorkspaceMode.Preview?Visibility.Visible:Visibility.Collapsed;
        if(value==WorkspaceMode.Map&&mapPane is null)
        {
            mapPane=new MapPane(settings,SaveSettings,SelectedMapPoint,StageLocation,SelectMapFile);
            Grid.SetColumn(mapPane,1);mediaWorkspace.Children.Insert(1,mapPane);
        }
        if(mapPane is not null)mapPane.Visibility=value==WorkspaceMode.Map?Visibility.Visible:Visibility.Collapsed;
        status.Text=transfer?$"{matches.Count(m=>m.Matched)} matched":$"{mediaItems.Count} files";
        UpdateMediaEmpty();UpdateControls();UpdateMap();Inspect();
    }

    private void UpdateMediaEmpty()
    {
        mediaEmpty.Visibility=mediaItems.Count==0?Visibility.Visible:Visibility.Collapsed;
        Grid.SetColumnSpan(mediaEmpty,mode==WorkspaceMode.Map?1:2);
        mediaList.Visibility=mediaItems.Count==0?Visibility.Collapsed:Visibility.Visible;
        if(mode==WorkspaceMode.Metadata)inspector.Visibility=mediaList.Visibility;
        if(mapPane is not null)mapPane.Visibility=mode==WorkspaceMode.Map?Visibility.Visible:Visibility.Collapsed;
    }

    private void ChooseMedia()
    {
        if(busy)return;
        var picker=new OpenFileDialog{Multiselect=true,Title="Add photos or videos",Filter="All files|*.*"};
        if(picker.ShowDialog(this)==true)_=AddMedia(picker.FileNames);
    }

    private void ChooseMediaFolder()
    {
        if(busy)return;
        var picker=new OpenFolderDialog{Title="Add a media folder"};
        if(picker.ShowDialog(this)==true)_=AddMedia([picker.FolderName]);
    }

    private async Task AddMedia(string[] paths)
    {
        if(busy)return;
        var inputs=mediaInputs.Concat(paths).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await RunOperation(async token=>
        {
            await ReadMedia(inputs,token);
            mediaInputs=inputs;
        },"Reading media metadata…");
    }

    private async Task ReadMedia(string[] inputs,CancellationToken token)
    {
        var tool=exif;
        var paths=await Task.Run(()=>Matching.Expand(inputs,token).ToArray(),token);
        var rows=await Task.WhenAll(paths.Select(async path=>
        {
            var item=new WorkspaceMedia(path);
            if(tool is not null)await ReloadItem(item,tool,token);
            return item;
        }));
        token.ThrowIfCancellationRequested();
        syncingMedia=true;
        try
        {
            mediaItems.Clear();
            foreach(var row in rows)mediaItems.Add(row);
            mediaSelection.IntersectWith(paths);
            if(mediaSelection.Count==0&&rows.Length>0)mediaSelection.Add(rows[0].Path);
            foreach(var row in rows.Where(row=>mediaSelection.Contains(row.Path)))mediaList.SelectedItems.Add(row);
        }
        finally{syncingMedia=false;}
        status.Text=rows.Length==0?"No supported media found":tool is null?"Choose ExifTool in Settings to load metadata.":$"{rows.Length} files";
        UpdateMediaEmpty();UpdatePending();UpdateMap();
    }

    private static async Task ReloadItem(WorkspaceMedia item,ExifService tool,CancellationToken token)
    {
        try
        {
            var tags=await tool.ReadAsync(item.Path,token);
            var point=await tool.LocationAsync(item.Path,token);
            item.SetMetadata(tags,point);
        }
        catch(OperationCanceledException) { throw; }
        catch(Exception e) { item.SetReadError(e.Message); }
    }

    private void UpdatePendingRows()
    {
        var pending=PendingPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach(var item in mediaItems)item.SetDraft(pending.Contains(item.Path), locationDrafts.GetValueOrDefault(item.Path));
    }

    private GeoPoint? SelectedMapPoint() => mediaSelection.Count==1
        ? mediaItems.FirstOrDefault(item=>mediaSelection.Contains(item.Path))?.EffectiveLocation : null;

    private void StageLocation(double latitude,double longitude)
    {
        if(busy||exif is null||mediaSelection.Count==0||!(latitude is >= -90 and <= 90&&longitude is >= -180 and <= 180))return;
        foreach(var path in mediaSelection)locationDrafts[path]=new GeoPoint(path,latitude,longitude);
        status.Text=$"Location staged for {mediaSelection.Count} files";
        UpdatePending();UpdateMap();
    }

    private void NavigateMedia(int offset)
    {
        var rows=mediaList.Items.Cast<WorkspaceMedia>().ToArray();
        var index=Array.IndexOf(rows,mediaList.SelectedItem as WorkspaceMedia);
        if(index<0&&offset<0)index=rows.Length;
        var target=index+offset;
        if(target<0||target>=rows.Length)return;
        mediaList.SelectedItem=rows[target];
        mediaList.ScrollIntoView(rows[target]);
    }

    private void SelectMapFile(string path)
    {
        var item=mediaItems.FirstOrDefault(item=>item.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
        if(item is null)return;
        mediaList.SelectedItem=item;
        mediaList.ScrollIntoView(item);
    }

    private void UpdateMap(bool focus=false)
    {
        if(mode!=WorkspaceMode.Map||mapPane is null)return;
        mapPane.SetFiles(mediaItems.Select(item=>item.EffectiveLocation).OfType<GeoPoint>().ToArray(),
            mediaSelection.ToHashSet(StringComparer.OrdinalIgnoreCase), locationDrafts.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), !busy&&exif is not null, focus);
    }

    private async void OpenMapFromTransfer()
    {
        if(busy)return;
        var paths=SelectedPaths;
        SetMode(WorkspaceMode.Map);
        await AddMedia(paths);
        if(paths.Length==0)return;
        mediaList.SelectedItems.Clear();
        foreach(var item in mediaItems.Where(item=>paths.Contains(item.Path,StringComparer.OrdinalIgnoreCase)))mediaList.SelectedItems.Add(item);
    }
}

internal sealed class WorkspaceMedia(string path) : INotifyPropertyChanged
{
    public string Path { get; }=path;
    public string Name=>System.IO.Path.GetFileName(Path);
    public Dictionary<string,string> Tags { get; private set; }=[];
    public GeoPoint? Location { get; private set; }
    private GeoPoint? draftLocation;
    private bool pending, thumbnailRequested;
    private string? readError;
    public GeoPoint? EffectiveLocation=>draftLocation??Location;
    public ImageSource? Thumbnail { get; private set; }
    public int TagCount=>Tags.Keys.Count(tag=>tag!="SourceFile"&&!tag.StartsWith("System:")&&!tag.StartsWith("ExifTool:"));
    public string Make=>Tag("Make");
    public string CaptureDate=>Tag("DateTimeOriginal","QuickTime:CreateDate","Keys:CreationDate","CreateDate");
    public string Coordinates=>EffectiveLocation is { } point?string.Create(CultureInfo.InvariantCulture,$"{point.Latitude:F7}, {point.Longitude:F7}"):"";
    public (int HasLocation,double Latitude,double Longitude) GpsOrder => EffectiveLocation is { } point
        ? (1,point.Latitude,point.Longitude) : (0,0,0);
    public string Status=>pending?"Pending":readError is not null?"Read error":"";
    public event PropertyChangedEventHandler? PropertyChanged;
    private string Tag(params string[] names)=>names.Select(name=>Tags.GetValueOrDefault(name)??
        Tags.FirstOrDefault(pair=>pair.Key.EndsWith(":"+name,StringComparison.Ordinal)).Value).FirstOrDefault(value=>!string.IsNullOrWhiteSpace(value))??"";
    public void SetMetadata(Dictionary<string,string> tags,GeoPoint? point){Tags=tags;Location=point;readError=null;Changed();}
    public void SetReadError(string message){readError=message;Changed();}
    public void SetDraft(bool hasDraft,GeoPoint? point){if(pending!=hasDraft||draftLocation!=point){pending=hasDraft;draftLocation=point;Changed();}}
    public async void LoadThumbnail(){if(thumbnailRequested)return;thumbnailRequested=true;Thumbnail=await Thumbnails.Load(Path,96);Changed();}
    private void Changed()=>PropertyChanged?.Invoke(this,new PropertyChangedEventArgs(null));
}
