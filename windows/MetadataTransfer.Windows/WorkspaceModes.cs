using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MetadataTransfer.Core;
using Microsoft.Win32;

namespace MetadataTransfer.Windows;

public sealed partial class MainWindow
{
    private enum WorkspaceMode { Metadata, Map, Transfer }
    private WorkspaceMode mode = WorkspaceMode.Metadata;
    private readonly Grid workspaces = new();
    private readonly Grid mediaWorkspace = new();
    private readonly Border mediaStripes = new() { IsHitTestVisible=false };
    private readonly ColumnDefinition mediaFilesColumn = new();
    private readonly ColumnDefinition mediaGutter = new();
    private readonly ColumnDefinition mediaDetail = new();
    private readonly Dictionary<DataGridColumn,string> mediaColumnKeys = [];
    private bool applyingColumns;
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
        SelectionUnit=DataGridSelectionUnit.FullRow, RowHeight=UI.FileRowHeight, GridLinesVisibility=DataGridGridLinesVisibility.None,
        HeadersVisibility=DataGridHeadersVisibility.Column, Background=Brushes.Transparent, BorderThickness=new Thickness(0),
        RowBackground=Brushes.Transparent, Margin=new Thickness(10,5,10,6), EnableRowVirtualization=true
    };
    private readonly StackPanel mediaEmpty = new() { HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center };
    private Button addFiles = null!, saveAll = null!, clearFiles = null!, mapFavourites = null!;
    private string[] mediaInputs = [];
    private readonly HashSet<string> excludedMedia = new(StringComparer.OrdinalIgnoreCase);
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
        mediaWorkspace.ColumnDefinitions.Add(mediaFilesColumn);
        mediaWorkspace.ColumnDefinitions.Add(mediaGutter);
        mediaWorkspace.ColumnDefinitions.Add(mediaDetail);
        var splitter = new GridSplitter
        {
            HorizontalAlignment=HorizontalAlignment.Stretch, VerticalAlignment=VerticalAlignment.Stretch,
            Background=Brushes.Transparent, ResizeBehavior=GridResizeBehavior.PreviousAndNext,
            ResizeDirection=GridResizeDirection.Columns, ShowsPreview=true
        };
        System.Windows.Automation.AutomationProperties.SetName(splitter, "Resize files and details");
        splitter.DragCompleted+=(_,e)=> { if(!e.Canceled) SaveMediaPaneWidth(); };
        splitter.KeyUp+=(_,e)=> { if(e.Key is Key.Left or Key.Right) SaveMediaPaneWidth(); };
        Grid.SetColumn(splitter,1);
        mediaWorkspace.Children.Add(splitter);
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
        thumbnail.SetValue(Image.StretchProperty,Stretch.UniformToFill);
        thumbnail.SetValue(FrameworkElement.MarginProperty,new Thickness(4,0,8,0));
        var filename = new FrameworkElementFactory(typeof(DockPanel));
        filename.AppendChild(thumbnail);
        var labels = new FrameworkElementFactory(typeof(StackPanel));
        labels.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
        foreach(var property in new[]{nameof(WorkspaceMedia.Name),nameof(WorkspaceMedia.Folder)})
        {
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetBinding(TextBlock.TextProperty,new Binding(property));
            label.SetValue(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis);
            if(property==nameof(WorkspaceMedia.Folder)) label.SetValue(OpacityProperty,0.68);
            labels.AppendChild(label);
        }
        var dot=new FrameworkElementFactory(typeof(TextBlock));
        dot.SetValue(TextBlock.TextProperty,"●");dot.SetValue(TextBlock.ForegroundProperty,UI.Orange);
        dot.SetValue(DockPanel.DockProperty,Dock.Right);dot.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
        dot.SetBinding(VisibilityProperty,new Binding(nameof(WorkspaceMedia.HasPending)){Converter=new BooleanToVisibilityConverter()});
        filename.AppendChild(dot);
        filename.AppendChild(labels);
        var fileColumn = new DataGridTemplateColumn
        {
            Header="Filename", Width=new DataGridLength(260,DataGridLengthUnitType.Pixel), MinWidth=180,
            SortMemberPath=nameof(WorkspaceMedia.Name), CellTemplate=new DataTemplate{VisualTree=filename}
        };
        mediaColumnKeys.Add(fileColumn,nameof(WorkspaceMedia.Name));mediaList.Columns.Add(fileColumn);
        AddColumn("Tags",nameof(WorkspaceMedia.TagCount),new DataGridLength(55),48);
        AddColumn("Camera make",nameof(WorkspaceMedia.Make),new DataGridLength(130),85);
        AddColumn("Capture date",nameof(WorkspaceMedia.CaptureDate),new DataGridLength(170),110);
        AddColumn("GPS coordinates",nameof(WorkspaceMedia.Coordinates),new DataGridLength(175),150,
            nameof(WorkspaceMedia.GpsOrder));
        foreach(var (label,property) in new (string,string)[]
        {
            ("Road / Location",nameof(WorkspaceMedia.Road)), ("City",nameof(WorkspaceMedia.City)),
            ("State / Region",nameof(WorkspaceMedia.Region)), ("Country",nameof(WorkspaceMedia.Country)),
            ("Altitude",nameof(WorkspaceMedia.Altitude)), ("Camera model",nameof(WorkspaceMedia.Model)),
            ("Lens",nameof(WorkspaceMedia.Lens)), ("Dimensions",nameof(WorkspaceMedia.Dimensions)),
            ("File size",nameof(WorkspaceMedia.Size)), ("File type",nameof(WorkspaceMedia.Format)),
            ("ISO",nameof(WorkspaceMedia.Iso)), ("Aperture",nameof(WorkspaceMedia.Aperture)),
            ("Shutter speed",nameof(WorkspaceMedia.Shutter)), ("Focal length",nameof(WorkspaceMedia.FocalLength))
        }) AddColumn(label,property,new DataGridLength(125),65);
        mediaList.ColumnReordered+=(_,_)=> { if(!applyingColumns) SaveMediaColumns(); };
        mediaList.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader),mediaList.ColumnHeaderStyle);
        mediaList.ColumnHeaderStyle.Setters.Add(new EventSetter(UIElement.PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler((sender,e)=>
            {
                var menu=CreateColumnMenu();
                menu.PlacementTarget=(DataGridColumnHeader)sender;
                menu.IsOpen=true;
                e.Handled=true;
            })));
        mediaList.ColumnHeaderStyle.Setters.Add(new Setter(Control.BackgroundProperty,Brushes.Transparent));
        mediaList.ColumnHeaderStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty,30d));
        mediaList.SelectionChanged+=(_,_)=>
        {
            if(syncingMedia||mode==WorkspaceMode.Transfer)return;
            mediaSelection.Clear();
            mediaSelection.UnionWith(mediaList.SelectedItems.Cast<WorkspaceMedia>().Select(item=>item.Path));
            UpdateControls();UpdateMap(true);Inspect();
        };
        mediaList.PreviewKeyDown+=(_,e)=>
        {
            if(e.Key==Key.Space){Preview();e.Handled=true;}
            else if(e.Key==Key.Delete){RemoveSelectedMedia();e.Handled=true;}
        };
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
        var open = new MenuItem{Header="Open file"};
        open.Click+=(_,_)=>{ if(SelectedPaths.FirstOrDefault() is string path) UI.Open(path); };
        var copyPath = new MenuItem{Header="Copy paths"};
        copyPath.Click+=(_,_)=>SetMediaClipboard(string.Join(Environment.NewLine,SelectedPaths));
        var copyLocation = new MenuItem{Header="Copy location",InputGestureText="Ctrl+Shift+C"};
        copyLocation.Click+=(_,_)=>CopySelectedLocation();
        var pasteLocation = new MenuItem{Header="Paste location",InputGestureText="Ctrl+Shift+V"};
        pasteLocation.Click+=(_,_)=>PasteSelectedLocation();
        var remove = new MenuItem{Header="Remove from list",InputGestureText="Delete"};
        remove.Click+=(_,_)=>RemoveSelectedMedia();
        foreach(var item in new[]{quick,open,reveal,copyPath,workflow,copyLocation,pasteLocation,remove})menu.Items.Add(item);
        menu.Opened+=(_,_)=>
        {
            quick.IsEnabled=open.IsEnabled=reveal.IsEnabled=copyPath.IsEnabled=workflow.IsEnabled=remove.IsEnabled=!busy&&mediaSelection.Count>0;
            copyLocation.IsEnabled=!busy&&SelectedMapPoint() is not null;
            pasteLocation.IsEnabled=!busy&&exif is not null&&mediaSelection.Count>0;
        };
        mediaList.ContextMenu=menu;
        mediaStripes.Background=UI.Stripes();
        mediaWorkspace.Children.Add(mediaStripes);
        mediaWorkspace.Children.Add(mediaList);
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
        var column = new DataGridTextColumn
        {
            Header=header,Binding=new Binding(property),SortMemberPath=sortProperty??property,Width=width,MinWidth=minimum,
            ElementStyle=new Style(typeof(TextBlock)){Setters={new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis),new Setter(VerticalAlignmentProperty,VerticalAlignment.Center),new Setter(MarginProperty,new Thickness(4,0,8,0))}}
        };
        mediaColumnKeys.Add(column,property);
        mediaList.Columns.Add(column);
    }

    private static string[] DefaultMediaColumns(WorkspaceMode workspaceMode) => workspaceMode==WorkspaceMode.Map
        ? [nameof(WorkspaceMedia.Name),nameof(WorkspaceMedia.Coordinates)]
        : [nameof(WorkspaceMedia.Name),nameof(WorkspaceMedia.TagCount),nameof(WorkspaceMedia.Make),
            nameof(WorkspaceMedia.CaptureDate),nameof(WorkspaceMedia.Coordinates)];

    private void ApplyMediaColumns()
    {
        var keys = settings.MediaColumns.TryGetValue(mode.ToString(),out var saved)
            ? saved : DefaultMediaColumns(mode).ToList();
        var visible = keys.Prepend(nameof(WorkspaceMedia.Name)).Distinct().ToHashSet();
        applyingColumns=true;
        try
        {
            int index=0;
            foreach(var key in keys.Concat(mediaColumnKeys.Values).Distinct())
            {
                var column = mediaColumnKeys.FirstOrDefault(pair=>pair.Value==key).Key;
                if(column is null)continue;
                column.DisplayIndex=index++;
                column.Visibility=visible.Contains(key)?Visibility.Visible:Visibility.Collapsed;
            }
            foreach(var sort in mediaList.Items.SortDescriptions.ToArray())
                if(!mediaList.Columns.Any(column=>column.Visibility==Visibility.Visible&&column.SortMemberPath==sort.PropertyName))
                {
                    mediaList.Items.SortDescriptions.Remove(sort);
                    foreach(var column in mediaList.Columns.Where(column=>column.SortMemberPath==sort.PropertyName))
                        column.SortDirection=null;
                }
        }
        finally { applyingColumns=false; }
    }

    private void SaveMediaColumns()
    {
        settings.MediaColumns[mode.ToString()]=mediaList.Columns.Where(column=>column.Visibility==Visibility.Visible)
            .OrderBy(column=>column.DisplayIndex).Select(column=>mediaColumnKeys[column]).ToList();
        SaveSettings();
    }

    private ContextMenu CreateColumnMenu()
    {
        var menu=new ContextMenu();
        foreach(var column in mediaList.Columns)
        {
            var item=new MenuItem
            {
                Header=column.Header,IsCheckable=true,IsChecked=column.Visibility==Visibility.Visible,
                IsEnabled=mediaColumnKeys[column]!=nameof(WorkspaceMedia.Name),StaysOpenOnClick=true
            };
            item.Click+=(_,_)=>{column.Visibility=item.IsChecked?Visibility.Visible:Visibility.Collapsed;SaveMediaColumns();ApplyMediaColumns();};
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        AddAction(menu,"Filename only",()=>{settings.MediaColumns[mode.ToString()]=[nameof(WorkspaceMedia.Name)];ApplyMediaColumns();SaveSettings();});
        AddAction(menu,"Reset columns",()=>{settings.MediaColumns.Remove(mode.ToString());ApplyMediaColumns();SaveSettings();});
        return menu;
    }

    private void SaveMediaPaneWidth()
    {
        if(mediaGutter.Width.Value==0)return;
        settings.MediaPaneWidth=mediaFilesColumn.ActualWidth;
        SaveSettings();
    }

    private void LayoutMediaPanes()
    {
        bool map=mode==WorkspaceMode.Map;
        bool show=map||settings.ShowInspector;
        double minimum=map?460:340;
        mediaFilesColumn.MinWidth=show?235:0;
        double windowWidth=mediaWorkspace.ActualWidth>0?mediaWorkspace.ActualWidth:Width;
        mediaFilesColumn.Width=show?new GridLength(Math.Clamp(settings.MediaPaneWidth>0?settings.MediaPaneWidth:(map?440:windowWidth-minimum-5),235,Math.Max(235,windowWidth-minimum-5))):new GridLength(1,GridUnitType.Star);
        mediaGutter.Width=new GridLength(show?5:0);
        mediaDetail.MinWidth=show?minimum:0;
        mediaDetail.Width=show?new GridLength(1,GridUnitType.Star):new GridLength(0);
        ConstrainMediaPane();
        if(!map)inspector.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        UpdatePreviewButton(settings.ShowInspector);
    }

    private void ConstrainMediaPane()
    {
        double minimum=mode==WorkspaceMode.Map?460:340;
        double width=mediaWorkspace.ActualWidth>0?mediaWorkspace.ActualWidth:Width;
        mediaFilesColumn.MaxWidth=mediaGutter.Width.Value>0?Math.Max(235,width-minimum-5):double.PositiveInfinity;
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
        for(var i=2;i<folderCommands.Children.Count;i++)folderCommands.Children[i].Visibility=transfer?Visibility.Visible:Visibility.Collapsed;
        copy.Visibility=transfer?Visibility.Visible:Visibility.Collapsed;
        previewPaneButton.Visibility=value==WorkspaceMode.Map?Visibility.Collapsed:Visibility.Visible;
        favouritesArrow.Visibility=mapFavourites.Visibility=value==WorkspaceMode.Map?Visibility.Visible:Visibility.Collapsed;
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
            Grid.SetColumn(inspector,2);mediaWorkspace.Children.Add(inspector);
        }
        inspector.SetWorkspaceEditing(value==WorkspaceMode.Metadata);
        if(!transfer){ApplyMediaColumns();LayoutMediaPanes();}
        if(value==WorkspaceMode.Map&&mapPane is null)
        {
            mapPane=new MapPane(settings,SaveSettings,SelectedMapPoint,StageLocation,SelectMapFiles);
            Grid.SetColumn(mapPane,2);mediaWorkspace.Children.Add(mapPane);
        }
        if(mapPane is not null)mapPane.Visibility=value==WorkspaceMode.Map?Visibility.Visible:Visibility.Collapsed;
        status.Text=transfer?$"{matches.Count(m=>m.Matched)} matched":$"{mediaItems.Count} files";
        UpdateMediaEmpty();UpdateControls();UpdateMap();Inspect();
    }

    private void UpdateMediaEmpty()
    {
        mediaEmpty.Visibility=mediaItems.Count==0?Visibility.Visible:Visibility.Collapsed;
        mediaList.Visibility=mediaItems.Count==0?Visibility.Collapsed:Visibility.Visible;
        // Draw only the empty tail; populated rows draw their own tint.
        int firstEmptyStripe=mediaItems.Count+mediaItems.Count%2;
        mediaStripes.Margin=new Thickness(10,35+firstEmptyStripe*UI.FileRowHeight,10,6);
        if(mapPane is not null)mapPane.Visibility=mode==WorkspaceMode.Map?Visibility.Visible:Visibility.Collapsed;
    }

    internal async Task CheckMediaWorkspaceAsync(string source)
    {
        SetMode(WorkspaceMode.Metadata);
        await AddMedia([source]);
        if(mediaItems.Count!=2||mediaSelection.Count!=1||
            mediaItems.Any(item=>item.Make!="Sony")||mediaGutter.Width.Value<=0)
            throw new InvalidOperationException("The shared media workspace did not load its fixture files and splitter.");
        var selection=SelectedPaths;
        ToggleInspector();
        if(inspector.Visibility!=Visibility.Collapsed||mediaGutter.Width.Value!=0||
            !SelectedPaths.SequenceEqual(selection))throw new InvalidOperationException("Collapsing the inspector changed the file selection.");
        ToggleInspector();
        if(inspector.Visibility!=Visibility.Visible||mediaGutter.Width.Value<=0)
            throw new InvalidOperationException("The inspector and splitter did not reopen.");
        RemoveSelectedMedia();
        await RunOperation(token=>ReadMedia(mediaInputs,token),"Reading metadata…");
        if(mediaItems.Count!=1||mediaItems.Any(item=>item.Path==selection[0]))
            throw new InvalidOperationException("A removed file reappeared when refreshing.");
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
            foreach(var path in await Task.Run(()=>Matching.Expand(paths,token).ToArray(),token))excludedMedia.Remove(path);
            mediaInputs=inputs;
            await ReadMedia(inputs,token);
        },"Reading media metadata…");
    }

    private async Task ReadMedia(string[] inputs,CancellationToken token)
    {
        var tool=exif;
        var paths=(await Task.Run(()=>Matching.Expand(inputs,token).ToArray(),token))
            .Where(path=>!excludedMedia.Contains(path)).ToArray();
        var existing=mediaItems.ToDictionary(item=>item.Path,StringComparer.OrdinalIgnoreCase);
        var rows=paths.Select(path=>existing.GetValueOrDefault(path)??new WorkspaceMedia(path)).ToArray();
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
        if(tool is null||rows.Length==0)return;
        progress.IsIndeterminate=false;
        int processed=0;
        foreach(var batch in rows.Chunk(60))
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var loaded=await tool.ReadWorkspaceBatchAsync(batch.Select(item=>item.Path).ToArray(),token);
                foreach(var item in batch)
                    if(loaded.TryGetValue(item.Path,out var data))item.SetMetadata(data.Tags,data.Location);
                    else item.SetReadError("ExifTool could not read the file.");
            }
            catch(Exception e) when(e is IOException or System.Text.Json.JsonException or System.ComponentModel.Win32Exception)
            {
                foreach(var item in batch)item.SetReadError(e.Message);
            }
            processed+=batch.Length;
            progress.Value=(double)processed/rows.Length;
            status.Text=$"Read metadata for {processed} of {rows.Length} files";
            UpdateMap();
        }
        status.Text=$"{rows.Length} files";
    }

    private void RemoveSelectedMedia()
    {
        if(busy||mode==WorkspaceMode.Transfer||mediaSelection.Count==0)return;
        var paths=SelectedPaths;
        if(paths.Any(path=>PendingPaths.Contains(path,StringComparer.OrdinalIgnoreCase))&&!ConfirmDiscardChanges(removing:true))return;
        RemoveMedia(paths);
    }

    private void ClearFiles()
    {
        if(busy)return;
        var paths=mode==WorkspaceMode.Transfer?originalFiles.Concat(exportFiles).ToArray():mediaItems.Select(item=>item.Path).ToArray();
        if(paths.Length==0)return;
        var affected=paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(PendingPaths.Any(affected.Contains)&&!ConfirmDiscardChanges(removing:true))return;
        if(mode==WorkspaceMode.Transfer)
        {
            foreach(var path in paths)DropDrafts(path);
            Clear(originals);Clear(exports);UpdateControls();Inspect();
        }
        else
        {
            mediaInputs=[];
            RemoveMedia(paths);
            excludedMedia.Clear();
        }
    }

    private void DropDrafts(string path)
    {
        drafts.Remove(path);locationDrafts.Remove(path);workflowDrafts.Remove(path);
    }

    private void RemoveMedia(string[] paths)
    {
        var removing=paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        syncingMedia=true;
        try
        {
            foreach(var path in paths){excludedMedia.Add(path);DropDrafts(path);}
            foreach(var item in mediaItems.Where(item=>removing.Contains(item.Path)).ToArray())mediaItems.Remove(item);
            mediaSelection.ExceptWith(paths);
        }
        finally { syncingMedia=false; }
        status.Text=$"{mediaItems.Count} files";
        UpdateMediaEmpty();UpdateControls();UpdateMap();Inspect();
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

    private GeoPoint? SelectedLocation() => mode!=WorkspaceMode.Transfer?SelectedMapPoint()
        : SelectedPaths is [var path]?locationDrafts.GetValueOrDefault(path)??inspector.LocationFor(path):null;

    private void SetMediaClipboard(string value)
    {
        try { Clipboard.SetText(value); }
        catch(System.Runtime.InteropServices.ExternalException) { status.Text="Clipboard is in use. Try again."; }
    }

    private void CopySelectedLocation()
    {
        if(!busy&&SelectedLocation() is { } point)
            SetMediaClipboard(FormattableString.Invariant($"{point.Latitude:F7}, {point.Longitude:F7}"));
    }

    private void PasteSelectedLocation()
    {
        if(busy||exif is null||SelectedPaths.Length==0)return;
        string[] coordinates;
        try { coordinates=Clipboard.GetText().Split(','); }
        catch(System.Runtime.InteropServices.ExternalException) { status.Text="Clipboard is in use. Try again.";return; }
        if(coordinates.Length==2&&
            double.TryParse(coordinates[0],NumberStyles.Float,CultureInfo.InvariantCulture,out var lat)&&
            double.TryParse(coordinates[1],NumberStyles.Float,CultureInfo.InvariantCulture,out var lon)&&
            lat is >= -90 and <= 90&&lon is >= -180 and <= 180)StageLocation(lat,lon);
        else status.Text="Clipboard has no valid latitude and longitude.";
    }

    private void StageLocation(double latitude,double longitude)
    {
        var paths=SelectedPaths;
        if(busy||exif is null||paths.Length==0||!(latitude is >= -90 and <= 90&&longitude is >= -180 and <= 180))return;
        foreach(var path in paths)locationDrafts[path]=new GeoPoint(path,latitude,longitude);
        status.Text=$"Location staged for {paths.Length} files";
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

    private void SelectMapFiles(string[] paths)
    {
        if(busy||mode!=WorkspaceMode.Map)return;
        var selected=paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var items=mediaItems.Where(item=>selected.Contains(item.Path)).ToArray();
        if(items.Length==0)return;
        syncingMedia=true;
        try
        {
            mediaList.SelectedItems.Clear();
            mediaSelection.Clear();
            foreach(var item in items)
            {
                mediaList.SelectedItems.Add(item);
                mediaSelection.Add(item.Path);
            }
        }
        finally { syncingMedia=false; }
        mediaList.ScrollIntoView(items[0]);
        UpdateControls();UpdateMap();
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
        SelectMapFiles(paths);
    }
}

internal sealed class WorkspaceMedia(string path) : INotifyPropertyChanged
{
    public string Path { get; }=path;
    public string Name=>System.IO.Path.GetFileName(Path);
    public string Folder=>System.IO.Path.GetDirectoryName(Path)??"";
    public Dictionary<string,string> Tags { get; private set; }=[];
    public GeoPoint? Location { get; private set; }
    private GeoPoint? draftLocation;
    private bool pending, thumbnailRequested;
    private string? readError;
    public GeoPoint? EffectiveLocation=>draftLocation??Location;
    public ImageSource? Thumbnail { get; private set; }
    public int TagCount=>Tags.Keys.Count(tag=>tag!="SourceFile"&&!tag.StartsWith("System:")&&!tag.StartsWith("ExifTool:"));
    public string Make=>Tag("Make");
    public string Model=>Tag("Model","CameraModelName");
    public string Lens=>Tag("LensModel","LensID","Lens");
    public string Dimensions=>Tag("ImageSize");
    public string Size=>Tag("FileSize");
    public string Format=>Tag("FileType");
    public string Iso=>Tag("ISO");
    public string Aperture=>Tag("FNumber","Aperture");
    public string Shutter=>Tag("ExposureTime","ShutterSpeed");
    public string FocalLength=>Tag("FocalLength");
    public string Road=>Tag("Sub-location","Location");
    public string City=>Tag("City");
    public string Region=>Tag("State","Province-State");
    public string Country=>Tag("Country","Country-PrimaryLocationName");
    public string Altitude=>Tag("GPSAltitude");
    public string CaptureDate=>Tag("DateTimeOriginal","QuickTime:CreateDate","Keys:CreationDate","CreateDate");
    public string Coordinates=>EffectiveLocation is { } point?string.Create(CultureInfo.InvariantCulture,$"{point.Latitude:F7}, {point.Longitude:F7}"):"";
    public (int HasLocation,double Latitude,double Longitude) GpsOrder => EffectiveLocation is { } point
        ? (1,point.Latitude,point.Longitude) : (0,0,0);
    public bool HasPending=>pending;
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
