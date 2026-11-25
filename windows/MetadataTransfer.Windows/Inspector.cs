using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MetadataTransfer.Core;
using System.IO;
namespace MetadataTransfer.Windows;

internal sealed class Inspector : DockPanel
{
    private readonly StackPanel content=new(){Margin=new Thickness(18)};
    private readonly StackPanel metadataControls=new();
    private readonly TextBox search=UI.Input("Search metadata");
    private readonly CheckBox edit=new(){Content="Edit metadata",Margin=new Thickness(4,10,0,10)};
    private readonly CheckBox differences=new(){Content="Differences only",Margin=new Thickness(4,4,0,8)};
    private readonly StackPanel groups=new();
    private readonly ComboBox sectionJump=new(){MinWidth=150,MaxHeight=32,VerticalAlignment=VerticalAlignment.Center,Visibility=Visibility.Collapsed};
    private readonly TextBlock heading=UI.Text("Preview",14,true);
    private readonly TextBlock title=UI.Text("No selection",16,true),description=UI.Muted("Select a file to preview its metadata.");
    private readonly Image image=new(){Height=172,Stretch=Stretch.Uniform,Margin=new Thickness(0,0,0,16)};
    private readonly Button save,revert;
    private readonly StackPanel editingControls=new();
    private readonly TextBlock count=UI.Muted("");
    private readonly TextBlock pendingLocation=UI.Muted("");
    private readonly TextBox tagName=UI.Input("Tag name, for example XMP:Artist"),tagValue=UI.Input("New tag value");
    private readonly HashSet<string> collapsed=["Other Metadata"];
    private MetadataField[] fields=[];private GeoPoint[] locations=[];private string[] paths=[];
    private bool locked, reading, canCompare, workspaceEditing;
    private readonly Button close;
    private int pending, imageVersion;
    private string? thumbnailPath, readMessage;
    public Action<string,string>? Stage;
    public Func<string,(string? Value,bool Mixed)>? DraftValue;
    public Action? SaveRequested,RevertRequested,HideRequested;
    public event Action<GeoPoint[]>? MapRequested;
    public Inspector()
    {
        var header=new DockPanel{Margin=new Thickness(18,10,12,10)};
        close=UI.IconButton("\uE76C","Hide preview",()=>HideRequested?.Invoke(),"Ctrl+I");
        DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        DockPanel.SetDock(sectionJump,Dock.Right);header.Children.Add(sectionJump);
        System.Windows.Automation.AutomationProperties.SetName(sectionJump,"Jump to metadata section");
        sectionJump.SelectionChanged+=(_,_)=> {
            if(sectionJump.SelectedIndex<=0||sectionJump.SelectedItem is not string name)return;
            sectionJump.SelectedIndex=0;
            if(groups.Children.OfType<Expander>().FirstOrDefault(group=>Equals(group.Header,name)) is { } target)
            {
                target.IsExpanded=true;
                target.Dispatcher.InvokeAsync(()=>target.BringIntoView());
            }
        };
        header.Children.Add(heading);
        DockPanel.SetDock(header,Dock.Top);Children.Add(header);
        var scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};Children.Add(scroll);
        image.Visibility = Visibility.Collapsed;
        content.Children.Add(image);content.Children.Add(title);description.Margin=new Thickness(0,6,0,14);content.Children.Add(description);
        content.Children.Add(metadataControls);
        metadataControls.Children.Add(UI.Muted("Search metadata"));search.Margin=new Thickness(0,4,0,6);
        metadataControls.Children.Add(search);
        metadataControls.Children.Add(UI.Horizontal(differences,edit));
        save=UI.Button("Save changes",()=>SaveRequested?.Invoke(),"Write changes to every selected file · Ctrl+S");revert=UI.Button("Revert",()=>RevertRequested?.Invoke());
        editingControls.Children.Add(UI.Muted("Blank values remove a tag when saved."));
        editingControls.Children.Add(UI.Horizontal(revert,save));editingControls.Children.Add(count);editingControls.Children.Add(pendingLocation);
        pendingLocation.Visibility=Visibility.Collapsed;
        var newTag=new Expander{Header="Add another tag",Margin=new Thickness(0,10,0,10)};
        var addPanel=new StackPanel();addPanel.Children.Add(UI.Muted("Tag name"));addPanel.Children.Add(tagName);addPanel.Children.Add(UI.Muted("Value"));addPanel.Children.Add(tagValue);
        addPanel.Children.Add(UI.Button("Add to selected files",()=>
        {
            var key=tagName.Text.Trim();if(!WorkflowRules.Valid("-"+key+"="+tagValue.Text)){MessageBox.Show("Enter a tag such as XMP:Artist.","Invalid tag");return;}
            Stage?.Invoke(key,tagValue.Text);tagName.Clear();tagValue.Clear();
        }));newTag.Content=addPanel;editingControls.Children.Add(newTag);content.Children.Add(editingControls);content.Children.Add(groups);
        edit.Checked+=(_,_)=>Render();edit.Unchecked+=(_,_)=>Render();search.TextChanged+=(_,_)=>Render();differences.Checked+=(_,_)=>Render();differences.Unchecked+=(_,_)=>Render();Render();
    }
    internal void EnableEditing()=>edit.IsChecked=true;
    public bool Editing => workspaceEditing || edit.IsChecked==true;
    public void SetWorkspaceEditing(bool enabled)
    {
        workspaceEditing=enabled;
        heading.Text=enabled?"Metadata":"Preview";
        close.Visibility=edit.Visibility=differences.Visibility=save.Visibility=enabled?Visibility.Collapsed:Visibility.Visible;
        Render();
    }
    public void SetBusy(bool value)
    {
        locked=value;
        groups.IsEnabled=!locked;
        UpdateControls();
    }
    public void SetPending(int changes)
    {
        pending=changes;
        count.Text=changes==0?"No unsaved changes":$"{changes} pending assignments";
        UpdateControls();
    }
    public void SetPendingLocation(GeoPoint[] locations)
    {
        var points=locations.Select(point=>(point.Latitude,point.Longitude)).Distinct().ToArray();
        pendingLocation.Visibility=points.Length==0?Visibility.Collapsed:Visibility.Visible;
        if(points.Length>0)
            UI.SetStatusText(pendingLocation,points.Length==1?FormattableString.Invariant($"Pending location · {points[0].Latitude:F6}, {points[0].Longitude:F6}"):"Multiple pending locations",UI.Orange);
    }
    private void UpdateControls()
    {
        bool available = !locked && !reading && readMessage is null && paths.Length > 0;
        search.IsEnabled = edit.IsEnabled = editingControls.IsEnabled = available;
        differences.IsEnabled = available && canCompare && !Editing;
        save.IsEnabled = revert.IsEnabled = available && pending > 0;
    }
    public Task SetSelection(string[] selected, string side)
    {
        paths=selected;fields=[];locations=[];canCompare=false;
        reading=paths.Length>0;readMessage=null;
        title.Text=paths.Length==0?"No selection":paths.Length==1?Path.GetFileName(paths[0]):$"{paths.Length} files selected";
        description.Text=paths.Length==0?"Select a file to preview its metadata.":paths.Length==1?paths[0]:$"Selected {side.ToLowerInvariant()}";
        description.ToolTip=string.Join(Environment.NewLine,paths);Render();
        return SetThumbnail(paths.Length==1&&!workspaceEditing?paths[0]:null);
    }

    private async Task SetThumbnail(string? path)
    {
        if (string.Equals(path,thumbnailPath,StringComparison.OrdinalIgnoreCase)) return;
        thumbnailPath=path;
        var version=++imageVersion;
        image.Source=null;
        image.Visibility=path is null?Visibility.Collapsed:Visibility.Visible;
        if(path is not null)
        {
            var thumb=await Thumbnails.Load(path,640);
            if(version==imageVersion)image.Source=thumb;
        }
    }
    public void SetMetadata(MetadataField[] values,GeoPoint[] pins,bool compare,string? error=null)
    {
        fields=values;locations=pins;canCompare=compare;reading=false;readMessage=error;
        Render();
    }
    public void RefreshFields()=>Render();
    private void Render()
    {
        metadataControls.Visibility=paths.Length>0?Visibility.Visible:Visibility.Collapsed;
        editingControls.Visibility=Editing&&paths.Length>0&&!reading&&readMessage is null?Visibility.Visible:Visibility.Collapsed;
        UpdateControls();
        groups.Children.Clear();
        sectionJump.Visibility=Visibility.Collapsed;
        if(paths.Length==0)return;
        if(reading||readMessage is not null)
        {
            groups.Children.Add(UI.Muted(readMessage??"Reading metadata…"));
            return;
        }
        var query=search.Text.Trim();
        bool differencesOnly=!Editing&&canCompare&&differences.IsChecked==true;
        var filtered=fields.Where(f=>(!differencesOnly||f.Different) &&
            (Editing||f.Mixed||f.Value.Length>0||f.Reference is not null) &&
            (query.Length==0||$"{f.Category} {Metadata.Readable(f.Name)} {f.Tag} {f.Value} {f.Reference}".Contains(query,StringComparison.OrdinalIgnoreCase))).ToArray();
        bool showLocation = query.Length == 0 || "Location GPS Map".Contains(query,StringComparison.OrdinalIgnoreCase);
        if(filtered.Length==0 && (!showLocation||workspaceEditing)){groups.Children.Add(UI.Muted("No matching metadata."));return;}
        foreach(var category in Metadata.Categories)
        {
            var values=filtered.Where(f=>f.Category==category).ToArray();
            bool showMap = !workspaceEditing && category == "Location" && showLocation;
            if (values.Length == 0 && !showMap) continue;
            var section=new Expander{Header=category,IsExpanded=!collapsed.Contains(category)||query.Length>0,Margin=new Thickness(0,8,0,4),FontWeight=FontWeights.SemiBold};
            section.Expanded+=(_,_)=>collapsed.Remove(category);section.Collapsed+=(_,_)=>collapsed.Add(category);
            var panel=new StackPanel{Margin=new Thickness(0,8,0,4),IsEnabled=!locked};
            if (showMap)
            {
                var label = locations.Length == 0 ? "Set location on map…" : "Change location on map…";
                panel.Children.Add(UI.Button(label, () => MapRequested?.Invoke(locations)));
            }
            foreach(var f in values)
            {
                var row=new Grid{Margin=new Thickness(0,5,0,5)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1.3,GridUnitType.Star)});
                var label=UI.Muted(Metadata.Readable(f.Name));label.ToolTip=f.Tag;label.VerticalAlignment=VerticalAlignment.Top;label.Margin=new Thickness(0,7,10,0);row.Children.Add(label);
                var valuePanel=new StackPanel();Grid.SetColumn(valuePanel,1);row.Children.Add(valuePanel);
                if(Editing&&f.WriteTag is not null)
                {
                    var underlying=fields.FirstOrDefault(x=>x.Tag==f.WriteTag)??f;
                    var (draft,mixedDrafts)=DraftValue?.Invoke(f.WriteTag)??default;
                    var box=UI.Input("Edit "+Metadata.Readable(f.Name),draft??(underlying.Mixed||mixedDrafts?"":underlying.Value));
                    box.FontSize=12;box.FontWeight=FontWeights.Normal;box.ToolTip=$"Writes {f.WriteTag} to {paths.Length} selected files. Empty means remove.";
                    var hint=UI.Muted(mixedDrafts?"Pending edits differ":draft is not null?"Unsaved":underlying.Mixed?"Multiple values":box.Text.Length==0?"Not set · enter a value":"",10);
                    if(mixedDrafts||underlying.Mixed&&draft is null)
                        UI.SetStatusText(hint,hint.Text,UI.Orange);
                    hint.Visibility=hint.Text.Length==0?Visibility.Collapsed:Visibility.Visible;
                    box.TextChanged+=(_,_)=>{Stage?.Invoke(f.WriteTag,box.Text);UI.SetStatusText(hint,"Unsaved",null);hint.Visibility=Visibility.Visible;};
                    valuePanel.Children.Add(box);valuePanel.Children.Add(hint);
                    if(f.Group=="Composite")valuePanel.Children.Add(UI.Muted("Edits "+Metadata.Readable(f.WriteTag.Split(':').Last()),9));
                }
                else
                {
                    var value=UI.Text(f.Display);
                    if(f.Mixed)UI.SetStatusText(value,f.Display,UI.Orange);
                    valuePanel.Children.Add(value);
                    if(paths.Length==1&&f.Different&&f.Reference is not null)valuePanel.Children.Add(UI.Muted("Other pane: "+f.Reference,10));
                    if(Editing)valuePanel.Children.Add(UI.Muted(f.Group=="Composite"?"Calculated · read-only":"Read-only",9));
                }
                panel.Children.Add(row);panel.Children.Add(new Border{Height=1,Background=UI.Subtle});
            }
            section.Content=panel;groups.Children.Add(section);
        }
        if(workspaceEditing)
        {
            var names=groups.Children.OfType<Expander>().Select(group=>group.Header).OfType<string>().ToArray();
            sectionJump.ItemsSource=names.Prepend("Jump to section…");
            sectionJump.SelectedIndex=0;
            sectionJump.Visibility=names.Length>0?Visibility.Visible:Visibility.Collapsed;
            sectionJump.ToolTip="Jump to section";
        }
    }
}
