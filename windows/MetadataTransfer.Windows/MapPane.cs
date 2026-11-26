using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using MetadataTransfer.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MetadataTransfer.Windows;

internal sealed class MapPane : DockPanel, IDisposable
{
    private const string MapHost = "joonga-metadata.example";
    private const string MapPage = "https://" + MapHost + "/index.html";
    private const string Attribution = "https://www.openstreetmap.org/copyright";
    private readonly WebView2 browser = new();
    private readonly TextBlock coordinates = UI.Text("Click the map to place a pin.");
    private readonly Settings settings;
    private readonly Action saveSettings;
    private readonly Func<GeoPoint?> sourcePoint;
    private readonly Action<double,double> stage;
    private readonly Action<string[]> selectFiles;
    private GeoPoint? lastPoint;
    private HashSet<string> selection = [], staged = [];
    private bool canWrite, initialized, fitted;
    private string? pinSignature;
    private readonly TextBox query = UI.Input("Search for a place or address");
    private readonly ComboBox places = new() { DisplayMemberPath = nameof(PlaceSearch.Result.Title), MaxDropDownHeight = 220 };
    private readonly TextBlock searchStatus = UI.Muted("");
    private readonly Button search;
    private CancellationTokenSource? pendingSearch;
    private GeoPoint[] locations = [];
    private bool closed;
    private bool ready;

    public MapPane(Settings settings, Action saveSettings, Func<GeoPoint?> sourcePoint,
        Action<double,double> stage, Action<string[]> selectFiles)
    {
        this.settings=settings;this.saveSettings=saveSettings;this.sourcePoint=sourcePoint;this.stage=stage;this.selectFiles=selectFiles;
        Margin=new Thickness(10,8,10,8);
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        search = UI.IconButton("\uE721","Search for a place", () => _ = FindPlaces());
        var options = UI.IconButton("\uE909","Map options");
        options.Click+=(_,_)=>OpenMapOptions(options);
        var searchRow = new DockPanel();
        DockPanel.SetDock(options, Dock.Right);
        searchRow.Children.Add(options);
        DockPanel.SetDock(search, Dock.Right);
        searchRow.Children.Add(search);
        var clearQuery=UI.IconButton("\uE711","Clear search",()=>{query.Clear();query.Focus();});
        clearQuery.Visibility=Visibility.Collapsed;
        DockPanel.SetDock(clearQuery,Dock.Right);
        searchRow.Children.Add(clearQuery);
        query.Margin = new Thickness(0, 3, 6, 3);
        searchRow.Children.Add(query);
        header.Children.Add(searchRow);
        System.Windows.Automation.AutomationProperties.SetName(places, "Place search results");
        places.Visibility = Visibility.Collapsed;
        places.Margin = new Thickness(0, 4, 0, 0);
        header.Children.Add(places);
        searchStatus.Visibility = Visibility.Collapsed;
        header.Children.Add(searchStatus);
        search.IsEnabled = false;
        query.IsEnabled = false;
        query.TextChanged += (_, _) =>
        {
            clearQuery.Visibility=query.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
            places.ItemsSource = null;
            places.Visibility = Visibility.Collapsed;
            SetSearchStatus(pendingSearch is null ? null : "Searching…");
            UpdateSearch();
        };
        query.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            _ = FindPlaces();
        };
        places.SelectionChanged += (_, _) =>
        {
            if (!ready || places.SelectedItem is not PlaceSearch.Result place) return;
            if(canWrite&&selection.Count>0)stage(place.Latitude,place.Longitude);
            FocusPoint(place.Latitude, place.Longitude);
            places.Visibility=Visibility.Collapsed;
        };

        var footer=new StackPanel{Margin=new Thickness(0,8,0,0)};
        footer.Children.Add(coordinates);
        SetDock(header,Dock.Top);SetDock(footer,Dock.Bottom);
        Children.Add(header);Children.Add(footer);
        var loading = UI.Muted("Loading map…", 13);
        loading.HorizontalAlignment = HorizontalAlignment.Center;
        var mapArea = new Grid { Background = UI.Subtle };
        browser.Visibility = Visibility.Hidden;
        mapArea.Children.Add(loading);
        mapArea.Children.Add(browser);
        Children.Add(mapArea);

        void MapUnavailable(string message)
        {
            coordinates.Text = "Map unavailable.";
            loading.Text = message;
            browser.Visibility = Visibility.Hidden;
        }

        Loaded += async (_, _) =>
        {
            if(initialized)return;
            initialized=true;
            try
            {
                // Reuse the existing tile cache and browser profile.
                var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Metadata Transfer", "WebView2");
                var environment = await CoreWebView2Environment.CreateAsync(null, profile);
                if (closed) return;
                await browser.EnsureCoreWebView2Async(environment);
                if (closed) return;
                var core = browser.CoreWebView2;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.UserAgent += " JoongaMetadata/1.0";
                core.SetVirtualHostNameToFolderMapping(MapHost, Path.Combine(AppContext.BaseDirectory, "Assets", "Map"),
                    CoreWebView2HostResourceAccessKind.Deny);
                core.NavigationStarting += (_, e) => e.Cancel = e.Uri != MapPage;
                core.NewWindowRequested += (_, e) =>
                {
                    e.Handled = true;
                    if (e.Uri == Attribution || e.Uri == "https://www.esri.com/en-us/legal/terms/full-master-agreement") UI.Open(e.Uri);
                };
                core.WebMessageReceived += OnMapMessage;
                core.NavigationCompleted += (_, e) =>
                {
                    if (e.IsSuccess)
                    {
                        browser.Visibility = Visibility.Visible;
                        loading.Visibility = Visibility.Collapsed;
                    }
                    else MapUnavailable("Could not load the location picker.");
                };
                core.Navigate(MapPage);
            }
            catch (Exception) when (closed) { }
            catch (Exception e)
            {
                MapUnavailable("The map requires Microsoft Edge WebView2 Runtime.\n" + e.Message);
            }
        };

    }

    private void SetSearchStatus(string? message)
    {
        searchStatus.Text = message ?? "";
        searchStatus.Visibility = message is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void UpdateSearch()
    {
        query.IsEnabled = ready;
        search.IsEnabled = ready && pendingSearch is null && !string.IsNullOrWhiteSpace(query.Text);
    }

    private async Task FindPlaces()
    {
        if (!search.IsEnabled) return;
        var text = query.Text.Trim();
        using var cancellation = new CancellationTokenSource();
        pendingSearch = cancellation;
        UpdateSearch();
        places.ItemsSource = null;
        places.Visibility = Visibility.Collapsed;
        SetSearchStatus("Searching…");
        try
        {
            var results = await PlaceSearch.FindAsync(text, cancellation.Token);
            if (closed || text != query.Text.Trim()) return;
            SetSearchStatus(results.Length == 0 ? "No places found. Try a more specific name or address." : null);
            if (results.Length == 0) return;
            places.ItemsSource = results;
            places.Visibility = Visibility.Visible;
            places.Focus();
            places.IsDropDownOpen = true;
        }
        catch (Exception)
        {
            if (!closed) SetSearchStatus("Place search is unavailable. You can still click the map to place a pin.");
        }
        finally
        {
            pendingSearch = null;
            if (!closed)
            {
                if (text != query.Text.Trim()) SetSearchStatus(null);
                UpdateSearch();
            }
        }
    }

    private void Post(object message) => browser.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message));

    private void OnMapMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (e.Source != MapPage) return;
        using var document = JsonDocument.Parse(e.WebMessageAsJson);
        var message = document.RootElement;
        if (message.ValueKind != JsonValueKind.Object || !message.TryGetProperty("kind", out var kind) ||
            kind.ValueKind != JsonValueKind.String) return;
        if (kind.GetString() == "ready")
        {
            ready = true;
            UpdatePins();
            SendStyle();
            UpdateActions();
            UpdateSearch();
        }
        else if (kind.GetString()=="file" && message.TryGetProperty("id",out var id) && id.ValueKind==JsonValueKind.String)
        {
            if(id.GetString() is string path && locations.Any(point=>point.Path==path))selectFiles([path]);
        }
        else if(kind.GetString()=="cluster"&&message.TryGetProperty("ids",out var ids)&&ids.ValueKind==JsonValueKind.Array)
        {
            var visible=locations.Where(point=>!settings.MapShowsSelectedOnly||selection.Contains(point.Path))
                .Select(point=>point.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var members=ids.EnumerateArray().Where(id=>id.ValueKind==JsonValueKind.String)
                .Select(id=>id.GetString()).OfType<string>().Where(visible.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if(members.Length>0)selectFiles(members);
        }
        else if (kind.GetString()=="request-thumbnail" && message.TryGetProperty("id",out var thumbnailId) &&
            thumbnailId.ValueKind==JsonValueKind.String && thumbnailId.GetString() is string thumbnailPath)
        {
            _ = SendThumbnailAsync(thumbnailPath);
        }
        else if (kind.GetString() is "pick" or "favourite-menu" &&
            message.TryGetProperty("latitude", out var lat) && lat.ValueKind == JsonValueKind.Number && lat.TryGetDouble(out var latitude) &&
            message.TryGetProperty("longitude", out var lon) && lon.ValueKind == JsonValueKind.Number && lon.TryGetDouble(out var longitude) &&
            latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180)
        {
            if (kind.GetString() == "favourite-menu")
            {
                var point = new GeoPoint("", latitude, longitude);
                var menu = new ContextMenu { PlacementTarget = this, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
                var item = new MenuItem { Header = "Save to Favourites…" };
                item.Click += (_, _) => SaveFavouriteAt(point);
                menu.Items.Add(item);
                menu.IsOpen = true;
                return;
            }
            lastPoint=new GeoPoint("",latitude,longitude);
            if(canWrite&&selection.Count>0)stage(latitude,longitude);
            UpdateActions();
        }
    }

    private async Task SendThumbnailAsync(string path)
    {
        string? image = null;
        if (locations.Any(point => point.Path == path))
        {
            try
            {
                if (await Thumbnails.Load(path, 96) is BitmapSource thumbnail)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(thumbnail));
                    using var buffer = new MemoryStream();
                    encoder.Save(buffer);
                    image = Convert.ToBase64String(buffer.ToArray());
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                // Files can become unavailable after the pins have loaded.
            }
        }
        if (!closed) Post(new { kind = "thumbnail", id = path, image });
    }

    public void SetFiles(GeoPoint[] points,HashSet<string> selected,HashSet<string> pending,bool editable,bool focus)
    {
        locations=points;selection=selected;staged=pending;canWrite=editable;
        UpdatePins();
        if(ready&&focus&&sourcePoint() is { } point)FocusPoint(point.Latitude,point.Longitude);
        UpdateActions();
    }

    private void UpdatePins()
    {
        if(!ready)return;
        var pins=locations.Where(point=>!settings.MapShowsSelectedOnly||selection.Contains(point.Path))
            .Select(point=>new { id=point.Path, name=Path.GetFileName(point.Path), latitude=point.Latitude,longitude=point.Longitude,
                pending=staged.Contains(point.Path),selected=selection.Contains(point.Path) }).ToArray();
        var signature=JsonSerializer.Serialize(pins);
        if(signature==pinSignature)return;
        pinSignature=signature;
        Post(new { kind="files",locations=pins,fit=!fitted });
        fitted|=pins.Length>0;
    }

    private void SendStyle()
    {
        if(ready)Post(new { kind="style",value=settings.MapStyle.ToLowerInvariant() });
    }

    private void OpenMapOptions(Button anchor)
    {
        var menu=new ContextMenu{PlacementTarget=anchor,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
        foreach(var value in new[]{"Normal","Hybrid","Satellite"})
        {
            var item=new MenuItem{Header=value,IsCheckable=true,IsChecked=settings.MapStyle==value};
            item.Click+=(_,_)=>{settings.MapStyle=value;saveSettings();SendStyle();};
            menu.Items.Add(item);
        }
        menu.Items.Add(new Separator());
        var selected=new MenuItem{Header="Show selected files only",IsCheckable=true,IsChecked=settings.MapShowsSelectedOnly};
        selected.Click+=(_,_)=>{settings.MapShowsSelectedOnly=selected.IsChecked;saveSettings();UpdatePins();};
        menu.Items.Add(selected);
        var fit=new MenuItem{Header="Fit visible pins",IsEnabled=ready&&locations.Any(point=>!settings.MapShowsSelectedOnly||selection.Contains(point.Path))};
        fit.Click+=(_,_)=>Post(new { kind="fit" });
        menu.Items.Add(fit);
        var centre=new MenuItem{Header="Use map centre",IsEnabled=ready&&canWrite&&selection.Count>0};
        centre.Click+=(_,_)=>Post(new { kind="pick-center" });
        menu.Items.Add(centre);
        menu.IsOpen=true;
    }

    private void UpdateActions()
    {
        coordinates.Text=sourcePoint() is { } point
            ? string.Create(CultureInfo.InvariantCulture,$"{point.Latitude:F6}, {point.Longitude:F6}")+(staged.Contains(point.Path)?" · Pending":"")
            : selection.Count==0?"Select a file to set its location":$"Click the map to set location for {selection.Count} selected";
    }

    private void FocusPoint(double latitude,double longitude)
    {
        lastPoint=new GeoPoint("",latitude,longitude);
        if(ready)Post(new { kind="focus",latitude,longitude });
    }

    public void OpenFavourites(Button anchor)
    {
        var menu=new ContextMenu{PlacementTarget=anchor,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
        MenuItem Item(string title,Action action,bool enabled=true)
        {
            var item=new MenuItem{Header=title.Replace("_","__"),IsEnabled=enabled};
            item.Click+=(_,_)=>action();
            return item;
        }
        menu.Items.Add(Item("Save current location…",SaveFavourite,sourcePoint() is not null||lastPoint is not null));
        foreach(var favourite in settings.MapFavourites)
        {
            var entry=new MenuItem{Header=favourite.Name.Replace("_","__")};
            entry.Items.Add(Item("Show on map",()=>FocusPoint(favourite.Latitude,favourite.Longitude)));
            entry.Items.Add(Item("Apply to selection",()=>{stage(favourite.Latitude,favourite.Longitude);FocusPoint(favourite.Latitude,favourite.Longitude);},canWrite&&selection.Count>0));
            entry.Items.Add(Item("Remove",()=>{settings.MapFavourites.Remove(favourite);saveSettings();}));
            menu.Items.Add(entry);
        }
        menu.IsOpen=true;
    }

    public void SaveFavourite()
    {
        if((lastPoint??sourcePoint()) is not { } point){SetSearchStatus("Pick a point on the map or select a geotagged file first.");return;}
        SaveFavouriteAt(point);
    }

    private void SaveFavouriteAt(GeoPoint point)
    {
        var dialog=UI.Window("Save favourite location",380,180,Window.GetWindow(this));
        dialog.MinHeight=160;
        var panel=new StackPanel{Margin=new Thickness(16)};
        panel.Children.Add(UI.Text("Location name"));
        var name=UI.Input("Name, e.g. Home");
        panel.Children.Add(name);
        var save=UI.Button("Save",()=>{if(!string.IsNullOrWhiteSpace(name.Text))dialog.DialogResult=true;});
        save.IsDefault=true;
        panel.Children.Add(UI.Horizontal(UI.Button("Cancel",dialog.Close),save));
        dialog.Content=panel;
        dialog.Loaded+=(_,_)=>name.Focus();
        if(dialog.ShowDialog()!=true)return;
        var title=name.Text.Trim();
        settings.MapFavourites.RemoveAll(item=>item.Name.Equals(title,StringComparison.OrdinalIgnoreCase));
        settings.MapFavourites.Add(new FavouriteLocation(title,point.Latitude,point.Longitude));
        saveSettings();
    }

    public void Dispose()
    { closed=true;pendingSearch?.Cancel();browser.Dispose(); }
}
