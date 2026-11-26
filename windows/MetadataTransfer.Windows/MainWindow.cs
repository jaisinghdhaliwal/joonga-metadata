using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Microsoft.Win32;
using MetadataTransfer.Core;
using System.IO;
using System.Reflection;

namespace MetadataTransfer.Windows;
public sealed partial class MainWindow : Window
{
    private readonly Settings settings;
    private readonly bool isolated;
    private ExifService? exif;
    private readonly WorkflowStore workflowStore=new(Path.Combine(Settings.Folder,"Workflows.json"));
    private readonly FilePane originals=new(true),exports=new(false);
    private readonly Inspector inspector=new();
    private readonly Grid workspace=new();
    private readonly ColumnDefinition inspectorColumn=new(){Width=new GridLength(350),MinWidth=290};
    private readonly ColumnDefinition inspectorDivider=new(){Width=new GridLength(5)};
    private double inspectorWidth=350;
    private readonly TextBlock status=UI.Muted("Drop files or folders into either pane"),selectionCount=UI.Muted("");
    private readonly ProgressBar progress=new(){Height=3,Minimum=0,Maximum=1,Visibility=Visibility.Collapsed};
    private readonly Button copy,cancel;
    private readonly Button pendingFiles=new(), favouritesArrow=new();
    private readonly Button previewPaneButton = new();
    private readonly StackPanel folderCommands = UI.Horizontal();
    private readonly Dictionary<string,Dictionary<string,string>> drafts=new(StringComparer.OrdinalIgnoreCase);
    private string[] originalInputs=[],exportInputs=[],originalFiles=[],exportFiles=[];private Match[] matches=[];
    private FilePane? active;private bool selectionGuard,busy;private CancellationTokenSource? operation,inspection;private int inspectionVersion;
    private string lastReport="";
    public MainWindow(bool isolated = false)
    {
        this.isolated=isolated;
        string? settingsError=null;try{settings=isolated?new Settings():Settings.Load();}catch(Exception e){settings=new();settingsError=e.Message;}
        Title=App.ProductName;Width=Math.Clamp(settings.Width,1040,2400);Height=Math.Clamp(settings.Height,620,1500);MinWidth=1040;MinHeight=620;ThemeMode=ThemeMode.System;WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=new FontFamily("Segoe UI Variable Text");FontSize=12;
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        exif=ExifService.Find(settings.ExifTool) is string executable?new(executable):null;
        var root=new DockPanel();var top=new StackPanel();DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
        top.Children.Add(CreateHeader());
        top.Children.Add(progress);
        var footer=new DockPanel{Margin=new Thickness(16,8,12,10)};copy=UI.Button("Copy metadata",()=>CopyMetadata());cancel=UI.Button("Cancel",()=>{operation?.Cancel();status.Text="Stopping…";});cancel.Visibility=Visibility.Collapsed;
        saveAll=UI.Button("Save changes",SaveEdits);
        saveAll.SetResourceReference(StyleProperty,"JoongaAccentButtonStyle");
        pendingFiles.Click+=(_,_)=>UI.Results(this,"Files with unsaved changes","Save Changes writes the staged metadata to these files."+Environment.NewLine+string.Join(Environment.NewLine,PendingPaths.OrderBy(path=>path).Select(path=>Path.GetFileName(path))));
        var right=UI.Horizontal(selectionCount,cancel,pendingFiles,saveAll,copy);DockPanel.SetDock(right,Dock.Right);footer.Children.Add(right);footer.Children.Add(status);DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
        workspace.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=280});workspace.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(5)});workspace.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star),MinWidth=280});workspace.ColumnDefinitions.Add(inspectorDivider);workspace.ColumnDefinitions.Add(inspectorColumn);
        workspace.Children.Add(originals);Grid.SetColumn(exports,2);workspace.Children.Add(exports);Grid.SetColumn(inspector,4);workspace.Children.Add(inspector);
        for(var i=1;i<4;i+=2){var splitter=new GridSplitter{Width=5,HorizontalAlignment=HorizontalAlignment.Stretch,VerticalAlignment=VerticalAlignment.Stretch,Background=Brushes.Transparent,ResizeBehavior=GridResizeBehavior.PreviousAndNext};Grid.SetColumn(splitter,i);workspace.Children.Add(splitter);}
        workspaces.Children.Add(workspace);workspaces.Children.Add(mediaWorkspace);
        root.Children.Add(workspaces);Content=root;
        InitializeModes();
        foreach(var pane in new[]{originals,exports})
        {
            pane.ChooseRequested+=()=>Choose(pane);pane.FilesDropped+=(paths,_)=>Receive(pane,paths);pane.ClearRequested+=()=>Clear(pane);
            pane.SelectionChanged+=()=>Selected(pane);pane.PreviewRequested+=Preview;pane.WorkflowRequested+=()=>OpenWorkflows();
            pane.CopyLocationRequested+=CopySelectedLocation;pane.PasteLocationRequested+=PasteSelectedLocation;
        }
        exports.DefaultRequested+=()=>{if(exports.Folder is null){status.Text="Choose a single exports folder first.";return;}settings.DefaultExports=exports.Folder;SaveSettings();status.Text="Default exports folder saved.";};
        exports.ClearDefaultRequested+=()=>{settings.DefaultExports=null;SaveSettings();status.Text="Default exports folder cleared.";};
        inspector.HideRequested=ToggleInspector;inspector.Stage=Stage;inspector.DraftValue=DraftValue;inspector.SaveRequested=SaveEdits;inspector.RevertRequested=()=>{foreach(var p in SelectedPaths){drafts.Remove(p);locationDrafts.Remove(p);workflowDrafts.Remove(p);}inspector.RefreshFields();UpdatePending();};
        inspector.MapRequested += _ => OpenMapFromTransfer();
        PreviewKeyDown+=OnKey;
        SizeChanged+=(_,_)=> { if(mode!=WorkspaceMode.Transfer) ConstrainMediaPane(); };
        Closing+=(_,e)=>
        {
            if(busy){UI.Dialog(this,"An operation is still running. Use Cancel and wait for the current file to finish before closing.","Operation in progress");e.Cancel=true;return;}
            if(PendingPaths.Length>0&&!ConfirmDiscardChanges()){e.Cancel=true;return;}
            inspection?.Cancel();
            if (!uninstalling) { settings.Width=RestoreBounds.Width;settings.Height=RestoreBounds.Height;SaveSettings(); }
        };
        SetMode(WorkspaceMode.Metadata);UpdateControls();
        Closed+=(_,_)=>mapPane?.Dispose();
        Loaded+=(_,_)=>
        {
            if(settingsError is not null)status.Text="Settings could not be loaded: "+settingsError;
            if(exif is null)status.Text="Choose ExifTool in Settings to read and write metadata.";
            if(settings.DefaultExports is string folder&&Directory.Exists(folder))Receive(exports,[folder]);
        };
    }
    private bool ConfirmDiscardChanges(bool removing = false)
    {
        var dialog = UI.Window("Unsaved changes", 408, 224, this);
        dialog.SizeToContent = SizeToContent.Height;
        dialog.MinHeight = 0;
        dialog.ResizeMode = ResizeMode.NoResize;
        dialog.ShowInTaskbar = false;
        dialog.ThemeMode = ThemeMode;
        var content = new StackPanel { Margin = new Thickness(24, 20, 24, 18) };
        content.Children.Add(UI.Text("Discard unsaved changes?", 20, true));
        var detail = UI.Text(removing ? "Pending edits for files you remove from the list will be discarded." :
            "Your pending metadata edits will be lost when you close Joonga Tag.", 13);
        detail.Margin = new Thickness(0, 8, 0, 20);
        content.Children.Add(detail);

        var discard = UI.Button("Discard changes", () => dialog.DialogResult = true);
        var keep = UI.Button("Keep editing", () => dialog.DialogResult = false);
        keep.IsDefault = true;
        keep.IsCancel = true;
        var actions = UI.Horizontal(discard, keep);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        content.Children.Add(actions);
        dialog.Content = content;
        dialog.Loaded += (_, _) => keep.Focus();
        return dialog.ShowDialog() == true;
    }

    internal async Task LoadSmokeAsync(string source,string destination)
    {
        SetMode(WorkspaceMode.Transfer);originalInputs=[source];exportInputs=[destination];originalFiles=Matching.Expand(originalInputs).ToArray();exportFiles=Matching.Expand(exportInputs).ToArray();
        originals.Folder=source;exports.Folder=destination;Compare();
        exports.List.SelectAll();inspector.EnableEditing();await InspectAsync();
    }
    private UIElement CreateHeader()
    {
        addFiles=UI.IconButton("\uE710", "Add files", ChooseMedia, "Ctrl+O");
        folderCommands.Children.Add(addFiles);
        clearFiles=UI.IconButton("\uE74D","Clear all files",ClearFiles);
        folderCommands.Children.Add(clearFiles);
        folderCommands.Children.Add(UI.IconButton("\uE838", "Choose originals folder", () => Choose(originals), "Ctrl+O"));
        folderCommands.Children.Add(UI.IconButton("\uE8DE", "Choose exports folder", () => Choose(exports), "Ctrl+Shift+O"));

        var separator = new Border { Width = 1, Height = 18, Background = UI.Subtle, Margin = new Thickness(7, 0, 7, 0) };
        var workflows = UI.IconButton("\uEF90", "Workflows");
        workflows.Content = UI.Horizontal(UI.Glyph("\uEF90"), new TextBlock
        {
            Text = "\uE70D",
            FontFamily = UI.IconFont,
            FontSize = 9,
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        workflows.Width = 52;
        workflows.Click += (_, _) => ShowWorkflowMenu(workflows);

        UI.StyleIconButton(previewPaneButton, "\uE8FF", "Preview pane", "Ctrl+I");
        UpdatePreviewButton(true);
        previewPaneButton.Click += (_, _) => ToggleInspector();
        mapFavourites=UI.IconButton("\uE734","Add favourite location");
        mapFavourites.Click+=(_,_)=>mapPane?.SaveFavourite();
        UI.StyleIconButton(favouritesArrow,"\uE70D","Manage favourite locations");
        favouritesArrow.Width=28;
        favouritesArrow.Click+=(_,_)=>mapPane?.OpenFavourites(favouritesArrow);

        var more = UI.IconButton("\uE712", "More actions");
        more.Click += (_, _) => ShowMoreActions(more);
        var left = UI.Horizontal(folderCommands, separator, workflows);
        var right = UI.Horizontal(
            UI.IconButton("\uE72C", "Refresh", Refresh, "F5"),
            mapFavourites,favouritesArrow,
            previewPaneButton,
            UI.IconButton("\uE713", "Settings", ShowSettings, "Ctrl+,"),
            more);
        return new WindowHeader(this, left, right, modePicker);
    }

    private void ShowWorkflowMenu(Button button)
    {
        var menu = new ContextMenu();
        AddAction(menu, "Manage workflows…", () => OpenWorkflows(), !busy);
        AddAction(menu, "New workflow", () => OpenWorkflows(true), !busy, "Ctrl+N");
        AddAction(menu, "Open workflow library folder", OpenWorkflowLibrary);
        PopulateSavedWorkflows(menu);
        OpenPopup(button, menu);
    }

    private void ShowMoreActions(Button button)
    {
        var menu = new ContextMenu();
        if(mode!=WorkspaceMode.Transfer)AddAction(menu,"Add folder…",ChooseMediaFolder,!busy);
        if(mode!=WorkspaceMode.Transfer)AddAction(menu,"Remove selected files",RemoveSelectedMedia,!busy&&mediaSelection.Count>0,"Delete");
        AddAction(menu,"Clear all files",ClearFiles,clearFiles.IsEnabled);
        AddAction(menu,"Copy location",CopySelectedLocation,!busy&&SelectedLocation() is not null,"Ctrl+Shift+C");
        AddAction(menu,"Paste location",PasteSelectedLocation,!busy&&exif is not null&&SelectedPaths.Length>0,"Ctrl+Shift+V");
        AddAction(menu, "Quick preview", Preview, SelectedPaths.Length > 0, "Space");
        AddAction(menu, "Previous file", () => Navigate(-1), !busy && (mode!=WorkspaceMode.Transfer?mediaItems.Count>0:NavigationTarget(-1) is not null), "Ctrl+Alt+[");
        AddAction(menu, "Next file", () => Navigate(1), !busy && (mode!=WorkspaceMode.Transfer?mediaItems.Count>0:NavigationTarget(1) is not null), "Ctrl+Alt+]");
        menu.Items.Add(new Separator());
        AddAction(menu, "Save metadata changes", SaveEdits,
            !busy && exif is not null && PendingPaths.Length>0, "Ctrl+S");
        AddAction(menu, "Last operation results", () => UI.Results(this, "Operation results", lastReport), lastReport.Length > 0);
        menu.Items.Add(new Separator());
        AddAction(menu, "About " + App.ProductName, () => AppUpdater.ShowAbout(this, () => !busy && PendingPaths.Length == 0));
        OpenPopup(button, menu);
    }

    private static void AddAction(ContextMenu menu, string title, Action action, bool enabled = true, string shortcut = "")
    {
        var item = new MenuItem { Header = title, IsEnabled = enabled, InputGestureText = shortcut };
        item.Click += (_, _) => action();
        menu.Items.Add(item);
    }

    private static void OpenPopup(Button button, ContextMenu menu)
    {
        menu.PlacementTarget = button;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private string[] SelectedPaths=>mode==WorkspaceMode.Transfer?active?.SelectedPaths??[]:mediaSelection.ToArray();
    private void OnKey(object sender,KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;
        if (modifiers == (ModifierKeys.Control | ModifierKeys.Alt))
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.OemOpenBrackets || key == Key.OemCloseBrackets)
            {
                Navigate(key == Key.OemOpenBrackets ? -1 : 1);
                e.Handled = true;
            }
        }
        else if(modifiers.HasFlag(ModifierKeys.Control))
        {
            if(modifiers==(ModifierKeys.Control|ModifierKeys.Shift)&&e.Key is Key.C or Key.V)
            {
                if(e.Key==Key.C)CopySelectedLocation();else PasteSelectedLocation();
                e.Handled=true;
            }
            else if(e.Key==Key.N){OpenWorkflows(true);e.Handled=true;}
            else if(e.Key==Key.S){SaveEdits();e.Handled=true;}
            else if(e.Key==Key.I){ToggleInspector();e.Handled=true;}
            else if(e.Key==Key.O){if(mode==WorkspaceMode.Transfer)Choose(Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)?exports:originals);else ChooseMedia();e.Handled=true;}
            else if(e.Key==Key.OemComma){ShowSettings();e.Handled=true;}
        }
        else if(e.Key==Key.F5){Refresh();e.Handled=true;}
    }
    private void SaveSettings(){if(isolated)return;try{settings.Save();}catch(Exception e){status.Text="Settings could not be saved: "+e.Message;}}
    private async Task ChooseExif(Window owner)
    {
        if(busy)return;var dialog=new OpenFileDialog{Title="Choose exiftool.exe",Filter="ExifTool executable (*.exe)|*.exe"};if(dialog.ShowDialog(owner)!=true)return;
        if(Path.GetFileName(dialog.FileName).Contains("(-k)",StringComparison.OrdinalIgnoreCase)){UI.Dialog(this,"Rename exiftool(-k).exe to exiftool.exe first. Keep its exiftool_files folder beside it.","ExifTool setup");return;}
        await ValidateExifTool(dialog.FileName);
    }
    private async Task ValidateExifTool(string requestedPath)
    {
        if(busy)return;
        var executable=requestedPath.Trim().Trim('"');
        if(!Path.IsPathFullyQualified(executable)||!File.Exists(executable)||
            !Path.GetExtension(executable).Equals(".exe",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Enter the full path to an ExifTool executable.");
        if(Path.GetFileName(executable).Contains("(-k)",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Rename exiftool(-k).exe to exiftool.exe first.");
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var (exitCode,output)=await new ExifService(executable).RunAsync(["-ver"],timeout.Token);
        if(exitCode!=0||!Version.TryParse(output.Trim(),out _))
            throw new IOException("The executable did not report a valid ExifTool version.");
        ApplyExifTool(executable);
    }
    private void ApplyExifTool(string? executable)
    {
        settings.ExifTool = executable;
        exif = ExifService.Find(executable) is string path ? new ExifService(path) : null;
        SaveSettings();
        status.Text = exif is null ? "ExifTool is unavailable." : "ExifTool configured.";
        UpdateControls();
        Inspect();
    }

    private void OpenWorkflowLibrary()
    {
        Directory.CreateDirectory(Settings.Folder);
        UI.Open(Settings.Folder);
    }

    private bool uninstalling;

    private void ShowSettings()
    {
        if (busy) return;
        var dialog = new SettingsWindow(this, settings, () => exif?.Executable, ChooseExif, ValidateExifTool,
            () => ApplyExifTool(null), SaveSettings, show =>
            {
                if (settings.ShowInspector != show) ToggleInspector();
                SaveSettings();
            }, OpenWorkflowLibrary, () => PendingPaths.Length == 0);
        dialog.ShowDialog();
        if (!dialog.UninstallRequested) return;
        var instances = System.Diagnostics.Process.GetProcessesByName(App.ProductName);
        bool otherInstance = instances.Any(process => process.Id != Environment.ProcessId);
        foreach (var process in instances) process.Dispose();
        if (otherInstance)
        {
            UI.Dialog(this, "Close other copies of Joonga Tag before uninstalling.", "Joonga Tag is open");
            return;
        }
        uninstalling = true;
        try { new UninstallWindow(fromSettings: true) { Owner=this }.ShowDialog(); }
        finally { uninstalling = false; }
    }

    private void Choose(FilePane pane)
    {
        if(busy)return;var dialog=new OpenFolderDialog{Title=pane.Originals?"Choose originals folder":"Choose exports folder",Multiselect=false};if(dialog.ShowDialog(this)==true)Receive(pane,[dialog.FolderName]);
    }
    private async void Receive(FilePane pane,string[] inputs)
    {
        if(busy)return;
        var folder=inputs.Length==1&&Directory.Exists(inputs[0])?Path.GetFullPath(inputs[0]):null;
        var prior=pane.Originals?originalInputs:exportInputs;
        // A single folder replaces the pane; loose files and multi-folder drops append.
        var next=folder is not null?inputs:prior.Concat(inputs).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await RunOperation(async token=>
        {
            var files=await Task.Run(()=>Matching.Expand(next,token).ToArray(),token);
            if(pane.Originals){originalInputs=next;originalFiles=files;}else{exportInputs=next;exportFiles=files;}
            pane.Folder=folder;Compare();
        },"Scanning media…");
    }
    private async void Refresh()
    {
        if(busy)return;
        if(mode!=WorkspaceMode.Transfer){await RunOperation(token=>ReadMedia(mediaInputs,token),"Reading metadata…");return;}await RunOperation(async token=>
        {
            var a=await Task.Run(()=>Matching.Expand(originalInputs,token).ToArray(),token);var b=await Task.Run(()=>Matching.Expand(exportInputs,token).ToArray(),token);originalFiles=a;exportFiles=b;Compare();
        },"Comparing files…");
    }
    private void Clear(FilePane pane)
    {
        if(busy)return;if(pane.Originals){originalInputs=[];originalFiles=[];}else{exportInputs=[];exportFiles=[];}pane.Folder=null;Compare();Inspect();
    }
    private void Compare()
    {
        var selected=(active?.SelectedPaths??[]).ToHashSet(StringComparer.OrdinalIgnoreCase);matches=Matching.Compare(originalFiles,exportFiles);
        selectionGuard=true;originals.SetRows(matches,originals.Folder,originalFiles.Length);exports.SetRows(matches,exports.Folder,exportFiles.Length);
        if (active is null || (active.Originals ? originalFiles.Length == 0 : exportFiles.Length == 0))
            active = exportFiles.Length > 0 ? exports : originals;
        foreach(var row in active.List.Items.Cast<FileRow>().Where(r=>r.Path is not null&&selected.Contains(r.Path)))active.List.SelectedItems.Add(row);
        if(active.List.SelectedItems.Count==0){var first=active.List.Items.Cast<FileRow>().FirstOrDefault(r=>r.Path is not null);if(first is not null)active.List.SelectedItem=first;}
        selectionGuard=false;if(mode==WorkspaceMode.Transfer)status.Text=$"{matches.Count(m=>m.Matched)} matched · {matches.Count(m=>!m.Matched&&m.Export is not null)} skipped";UpdateControls();
    }
    private void Selected(FilePane pane)
    {
        if(selectionGuard||mode!=WorkspaceMode.Transfer)return;selectionGuard=true;active=pane;(pane==originals?exports:originals).List.SelectedItems.Clear();selectionGuard=false;UpdateControls();Inspect();
    }
    private async void Inspect()=>await InspectAsync();
    private async Task InspectAsync()
    {
        inspection?.Cancel();
        using var request = new CancellationTokenSource();
        inspection = request;
        var token = request.Token;
        var version = ++inspectionVersion;
        var paths = SelectedPaths;
        var reader = exif;
        if(mode==WorkspaceMode.Map) { inspection=null; return; }
        var thumbnail = inspector.SetSelection(paths,mode==WorkspaceMode.Metadata?"Files":active==originals?"Originals":"Exports");
        UpdatePending();
        try
        {
            if(paths.Length==0)return;
            if(reader is null)
            {
                inspector.SetMetadata([],[],false,"Choose ExifTool in Settings to read metadata.");
                return;
            }
            if(mode==WorkspaceMode.Metadata)
            {
                var items=mediaItems.Where(item=>mediaSelection.Contains(item.Path)).ToArray();
                inspector.SetMetadata(Metadata.Build(items.Select(item=>item.Tags).ToArray(),[]),items.Select(i=>i.Location).OfType<GeoPoint>().ToArray(),false);
                return;
            }
            var selected=paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var references=matches.Where(m=>selected.Contains(m.Original??"")||selected.Contains(m.Export??"")).SelectMany(m=>new[]{m.Original,m.Export}).OfType<string>().Where(p=>!selected.Contains(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var docs=Task.WhenAll(paths.Select(p=>reader.ReadAsync(p,token)));
            var refs=Task.WhenAll(references.Select(p=>reader.ReadAsync(p,token)));
            var pins=Task.WhenAll(paths.Select(p=>reader.LocationAsync(p,token)));
            await Task.WhenAll(docs,refs,pins);
            if(version!=inspectionVersion||token.IsCancellationRequested)return;
            inspector.SetMetadata(Metadata.Build(await docs,await refs), (await pins).OfType<GeoPoint>().ToArray(), paths.Length>1||references.Length>0);
        }
        catch(OperationCanceledException) when(token.IsCancellationRequested){}
        catch(Exception e)
        {
            if(version==inspectionVersion&&!token.IsCancellationRequested)
                inspector.SetMetadata([],[],false,"Could not read metadata: "+e.Message);
        }
        finally
        {
            if(inspection==request)inspection=null;
            await thumbnail;
        }
    }
    private void Stage(string tag,string value)
    {
        foreach(var path in SelectedPaths){if(!drafts.TryGetValue(path,out var values))drafts[path]=values=new();values[tag]=value;}UpdatePending();
    }
    private (string? Value, bool Mixed) DraftValue(string tag)
    {
        var values=SelectedPaths.Select(p=>drafts.TryGetValue(p,out var d)&&d.TryGetValue(tag,out var v)?v:null).Distinct().ToArray();
        return (values.Length==1?values[0]:null,values.Length>1);
    }
    private void UpdatePending()
    {
        var paths=SelectedPaths;
        inspector.SetPending(paths.Sum(path=>(drafts.GetValueOrDefault(path)?.Count??0)+
            (workflowDrafts.GetValueOrDefault(path)?.Count??0)+(locationDrafts.ContainsKey(path)?1:0)));
        var locations=paths.Where(locationDrafts.ContainsKey).Select(path=>locationDrafts[path]).ToArray();
        inspector.SetPendingLocation(locations);
        var pending=PendingPaths.Length;
        var label=UI.Text("");UI.SetStatusText(label,$"{pending} pending",UI.Orange);
        pendingFiles.Content=label;
        pendingFiles.Visibility=pending>0?Visibility.Visible:Visibility.Collapsed;
        saveAll.IsEnabled=!busy&&exif is not null&&pending>0;
        UpdatePendingRows();
    }
    private async void SaveEdits()
    {
        if(busy||exif is not ExifService tool)return;
        var changes=PendingPaths.ToDictionary(path=>path,path=>(
            Operations:(workflowDrafts.GetValueOrDefault(path)??[]).Concat(
                (drafts.GetValueOrDefault(path)??[]).OrderBy(kv=>kv.Key).Select(kv=>$"-{kv.Key}={kv.Value}")).ToArray(),
            Location:locationDrafts.GetValueOrDefault(path)),StringComparer.OrdinalIgnoreCase);
        if(changes.Count==0)return;
        await Batch(changes.Keys.ToArray(),async path=>
        {
            var change=changes[path];
            var result=change.Location is { } point
                ? await tool.WriteLocationAsync(path,point.Latitude,point.Longitude,change.Operations)
                : await tool.WriteAsync(path,change.Operations);
            if(result.Success)
            {
                drafts.Remove(path);workflowDrafts.Remove(path);locationDrafts.Remove(path);
                var item=mediaItems.FirstOrDefault(item=>item.Path.Equals(path,StringComparison.OrdinalIgnoreCase));
                if(item is not null) await ReloadItem(item,tool,CancellationToken.None);
            }
            return result;
        },"Saving metadata");
        UpdateMap();
    }
    private async void CopyMetadata()
    {
        if(mode!=WorkspaceMode.Transfer||busy||exif is null)return;var pairs=matches.Where(m=>m.Matched).ToArray();if(pairs.Length==0)return;
        if(UI.Dialog(this,$"Copy writable metadata to {pairs.Length} exports?\n\nExifTool saves backups beside the modified files. Media streams are not re-encoded.","Copy metadata",MessageBoxButton.OKCancel,MessageBoxImage.Question)!=MessageBoxResult.OK)return;
        var lookup=pairs.ToDictionary(m=>m.Export!);await Batch(lookup.Keys.ToArray(),p=>exif.CopyAsync(lookup[p]),"Copying metadata");
    }
    private async Task Batch(string[] paths,Func<string,Task<OperationResult>> action,string title)
    {
        var results=new List<OperationResult>();
        await RunOperation(async token=>
        {
            for(var i=0;i<paths.Length;i++)
            {
                if(token.IsCancellationRequested)break;
                status.Text=$"{title} · {i+1} of {paths.Length}";progress.Value=(double)i/paths.Length;
                try{results.Add(await action(paths[i]));}catch(Exception e){results.Add(new(paths[i],false,e.Message));}
            }
            lastReport=string.Join("\n\n",results.Select(r=>$"{(r.Success?"SAVED":"REVIEW")} · {r.Path}\n{r.Output}"));
            if(results.Count<paths.Length)lastReport+=$"\n\nStopped. {paths.Length-results.Count} files were not processed.";
            status.Text=$"{results.Count(r=>r.Success)} saved · {results.Count(r=>!r.Success)} need review · {paths.Length-results.Count} not processed";
        },title, determinate: true);
        UI.Results(this,"Operation results",lastReport);
    }
    private async Task RunOperation(Func<CancellationToken,Task> action,string message,bool determinate = false)
    {
        if(busy)return;busy=true;operation=new();inspection?.Cancel();status.Text=message;progress.Visibility=Visibility.Visible;progress.IsIndeterminate=!determinate;progress.Value=0;cancel.Visibility=Visibility.Visible;UpdateControls();
        try{await action(operation.Token);}catch(OperationCanceledException){status.Text="Cancelled.";}catch(Exception e){status.Text=e.Message;UI.Results(this,"Operation failed",e.ToString());}
        finally{busy=false;operation.Dispose();operation=null;progress.Visibility=Visibility.Collapsed;cancel.Visibility=Visibility.Collapsed;UpdateControls();UpdateMap();Inspect();}
    }
    private void UpdateControls(){modePicker.IsEnabled=!busy;mediaWorkspace.IsEnabled=!busy;folderCommands.IsEnabled=!busy;originals.IsEnabled=!busy;exports.IsEnabled=!busy;inspector.SetBusy(busy);copy.IsEnabled=!busy&&exif is not null&&matches.Any(m=>m.Matched);clearFiles.IsEnabled=!busy&&(mode==WorkspaceMode.Transfer?originalFiles.Length+exportFiles.Length>0:mediaItems.Count>0);mapFavourites.IsEnabled=!busy;selectionCount.Text=$"{SelectedPaths.Length} selected   ";UpdatePending();}
    private void ToggleInspector()
    {
        if(busy)return;
        if(mode!=WorkspaceMode.Transfer)
        {
            settings.ShowInspector=!settings.ShowInspector;
            LayoutMediaPanes();
            SaveSettings();
            Inspect();
            return;
        }
        var show=inspector.Visibility!=Visibility.Visible;
        if(!show&&inspectorColumn.ActualWidth>=inspectorColumn.MinWidth)inspectorWidth=inspectorColumn.ActualWidth;
        inspector.Visibility=show?Visibility.Visible:Visibility.Collapsed;
        inspectorColumn.MinWidth=show?290:0;
        inspectorColumn.Width=new GridLength(show?inspectorWidth:0);
        inspectorDivider.Width=new GridLength(show?5:0);
        settings.ShowInspector=show;
        UpdatePreviewButton(show);
        SaveSettings();
    }
    private FileRow? NavigationTarget(int offset)
    {
        if (active is null) return null;
        var rows = active.List.Items.Cast<FileRow>().Where(row => row.CanSelect).ToList();
        var current = active.List.SelectedItem as FileRow;
        var index = current is null ? -1 : rows.IndexOf(current);
        if (index < 0) return offset < 0 ? rows.LastOrDefault() : rows.FirstOrDefault();
        var destination = index + offset;
        return destination >= 0 && destination < rows.Count ? rows[destination] : null;
    }

    private void Navigate(int offset)
    {
        if(busy)return;
        if(mode!=WorkspaceMode.Transfer){NavigateMedia(offset);return;}
        if(active is null || NavigationTarget(offset) is not FileRow row)return;
        active.List.SelectedItem = row;
        active.List.ScrollIntoView(row);
    }

    private void UpdatePreviewButton(bool visible)
    {
        var sidebar = new System.Windows.Shapes.Path
        {
            Data=Geometry.Parse("M 2,3 L 16,3 16,15 2,15 Z M 11,3 L 11,15" +
                (visible ? " M 13,7 L 14.5,7 M 13,10 L 14.5,10" : "")),
            StrokeThickness=1.25, Width=18, Height=18
        };
        sidebar.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,
            new System.Windows.Data.Binding(nameof(Control.Foreground)) { Source=previewPaneButton });
        previewPaneButton.Content=sidebar;
        var label = visible ? "Hide preview pane" : "Show preview pane";
        previewPaneButton.ToolTip = label + " (Ctrl+I)";
        AutomationProperties.SetName(previewPaneButton, label);
    }
    private void Preview(){if(SelectedPaths.Length>0)new PreviewWindow(SelectedPaths,this).Show();}

    private void PopulateSavedWorkflows(ContextMenu menu)
    {
        menu.Items.Add(new Separator());

        Workflow[] saved;
        try
        {
            saved = workflowStore.Load()
                .OrderBy(workflow => workflow.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }
        catch (Exception error)
        {
            menu.Items.Add(new MenuItem { Header = "Could not load saved workflows", IsEnabled = false });
            status.Text = "Could not load workflows: " + error.Message;
            return;
        }
        if (saved.Length == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No saved workflows", IsEnabled = false });
            return;
        }

        var targetCount = SelectedPaths.Length;
        if (targetCount == 0)
            menu.Items.Add(new MenuItem { Header = "Select files to apply a workflow", IsEnabled = false });
        foreach (var workflow in saved)
        {
            var menuItem = new MenuItem
            {
                Header = workflow.Name.Replace("_", "__"),
                IsEnabled = !busy && exif is not null && targetCount > 0
            };
            menuItem.Click += async (_, _) => await ApplyWorkflow(workflow.Operations, SelectedPaths);
            menu.Items.Add(menuItem);
        }
    }

    private Task ApplyWorkflow(string[] operations, string[] paths)
    {
        if(!busy&&exif is not null&&paths.Length>0)
        {
            foreach(var path in paths)
            {
                if(!workflowDrafts.TryGetValue(path,out var pending))workflowDrafts[path]=pending=[];
                pending.AddRange(operations);
            }
            status.Text=$"Workflow staged for {paths.Length} files";
            UpdatePending();
        }
        return Task.CompletedTask;
    }

    private void OpenWorkflows(bool create=false)
    {
        if(busy)return;
        try
        {
            var scopes=mode==WorkspaceMode.Transfer ? new Dictionary<string,string[]>{{"Selected files",SelectedPaths},{"All exports",exportFiles},{"All originals",originalFiles}} : new Dictionary<string,string[]>{{"Selected files",SelectedPaths},{"All loaded files",mediaItems.Select(i=>i.Path).ToArray()}};
            var window=new WorkflowWindow(this,workflowStore,scopes,create);
            window.ApplyRequested += async (args, paths) => await ApplyWorkflow(args, paths);
            window.ShowDialog();
        }
        catch(Exception e){UI.Results(this,"Workflow library could not be opened",e.Message+"\n\nYour existing library has not been changed.");}
    }
}
