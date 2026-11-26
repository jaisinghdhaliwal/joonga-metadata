using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using MetadataTransfer.Core;
using System.ComponentModel;
using System.IO;
namespace MetadataTransfer.Windows;
internal sealed class WorkflowWindow : Window
{
    private readonly WorkflowStore store;
    private Workflow[] workflows;
    private readonly Dictionary<string,string[]> scopes;
    private readonly ListBox list=new(){SelectionMode=SelectionMode.Extended,DisplayMemberPath="Name",BorderThickness=new Thickness(0),Background=Brushes.Transparent};
    private readonly TextBox name=UI.Input("Preset name"),operations=UI.Input("Metadata assignments","",true),search=UI.Input("Search presets");
    private readonly TextBlock status=UI.Muted("Saved in Application Data · compatible with the Mac version"),validation=UI.Muted(""),targetCount=UI.Muted("");
    private readonly ComboBox scope=new(){MinWidth=180};
    private readonly Button save,apply,duplicate,delete;
    private readonly StackPanel editor=new(){Margin=new Thickness(20)};
    private readonly TextBlock empty=UI.Muted("Select a preset, or choose New preset to create one.",14);
    private Workflow? current;private bool creating,loading,allowClose;private string[] priorIDs=[];
    public event Action<string[],string[]>? ApplyRequested;
    private string[] Arguments=>WorkflowRules.Parse(operations.Text);
    private string[] Targets=>scope.SelectedItem is string key?scopes[key]:[];
    private bool Dirty=>current is not null?name.Text!=current.Name||!Arguments.SequenceEqual(current.Operations):creating&&(!string.IsNullOrEmpty(name.Text)||!string.IsNullOrEmpty(operations.Text));
    public WorkflowWindow(Window owner,WorkflowStore library,Dictionary<string,string[]> targets,bool newWorkflow=false)
    {
        store=library;workflows=store.Load();scopes=targets;Title="Presets";Owner=owner;Width=960;Height=740;MinWidth=820;MinHeight=640;ThemeMode=ThemeMode.System;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var root=new DockPanel();var toolbar=new DockPanel{Margin=new Thickness(16,10,16,10)};var actions=UI.Horizontal(UI.Button("Import…",Import),UI.Button("Export selected…",()=>Export(false)),UI.Button("Export all…",()=>Export(true)),UI.Button("Done",Close));DockPanel.SetDock(actions,Dock.Right);toolbar.Children.Add(actions);toolbar.Children.Add(UI.Text("Presets",18,true));DockPanel.SetDock(toolbar,Dock.Top);root.Children.Add(toolbar);
        status.Margin=new Thickness(16,10,16,10);DockPanel.SetDock(status,Dock.Bottom);root.Children.Add(status);
        var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(260)});body.ColumnDefinitions.Add(new ColumnDefinition());root.Children.Add(body);
        var sidebar=new DockPanel{Background=UI.Subtle};search.Margin=new Thickness(12);DockPanel.SetDock(search,Dock.Top);sidebar.Children.Add(search);
        save=UI.Button("Save preset",()=>Save());apply=UI.Button("Apply",Apply);duplicate=UI.Button("Duplicate",Duplicate);delete=UI.Button("Delete",Delete);
        var management=UI.Horizontal(UI.Button("New",New,"New preset · Ctrl+N"),duplicate,delete);management.Margin=new Thickness(8);DockPanel.SetDock(management,Dock.Bottom);sidebar.Children.Add(management);sidebar.Children.Add(list);body.Children.Add(sidebar);
        var detail=new Grid();Grid.SetColumn(detail,1);body.Children.Add(detail);detail.Children.Add(editor);empty.HorizontalAlignment=HorizontalAlignment.Center;empty.VerticalAlignment=VerticalAlignment.Center;empty.Margin=new Thickness(24);detail.Children.Add(empty);
        editor.Children.Add(UI.Muted("PRESET NAME",10));name.FontSize=18;name.Margin=new Thickness(0,6,0,16);editor.Children.Add(name);
        editor.Children.Add(UI.Text("Metadata assignments",12,true));operations.FontFamily=new FontFamily("Cascadia Mono, Consolas");operations.FontSize=13;operations.Height=270;operations.Margin=new Thickness(0,8,0,8);editor.Children.Add(operations);editor.Children.Add(validation);
        scope.ItemsSource=scopes.Keys;scope.SelectedIndex=0;scope.Margin=new Thickness(10,0,0,0);editor.Children.Add(UI.Horizontal(UI.Text("Stage for"),scope));targetCount.Margin=new Thickness(0,10,0,8);editor.Children.Add(targetCount);
        editor.Children.Add(UI.Button("Review target files",()=>UI.Results(this,"Target files",string.Join(Environment.NewLine,Targets))));
        editor.Children.Add(UI.Horizontal(save,apply));Content=root;
        list.SelectionChanged+=(_,_)=>SelectChanged();search.TextChanged+=(_,_)=>Filter();name.TextChanged+=(_,_)=>Update();operations.TextChanged+=(_,_)=>Update();scope.SelectionChanged+=(_,_)=>Update();
        list.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Delete){Delete();e.Handled=true;}};
        PreviewKeyDown+=(_,e)=>
        {
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&e.Key==Key.N){New();e.Handled=true;}
            if(Keyboard.Modifiers.HasFlag(ModifierKeys.Control)&&e.Key==Key.S){Save();e.Handled=true;}
            if(e.Key==Key.Escape){Close();e.Handled=true;}
        };
        Closing+=(_,e)=>{if(!allowClose&&!Resolve())e.Cancel=true;};Filter();Update();if(newWorkflow)Loaded+=(_,_)=>New();
    }
    private bool Resolve()
    {
        if(!Dirty)return true;var response=UI.Dialog(this,"Save changes to this preset?\n\nNo discards the changes.","Unsaved preset",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);
        return response==MessageBoxResult.No||response==MessageBoxResult.Yes&&Save();
    }
    private void SelectChanged()
    {
        if(loading)return;
        var next=list.SelectedItems.Cast<Workflow>().ToArray();
        if(!Resolve()) { loading=true;list.SelectedItems.Clear();foreach(var w in workflows.Where(w=>priorIDs.Contains(w.Id)))list.SelectedItems.Add(w);loading=false;return; }
        priorIDs=next.Select(w=>w.Id).ToArray();current=next.Length==1?workflows.FirstOrDefault(w=>w.Id==next[0].Id):null;creating=false;
        loading=true;
        list.SelectedItems.Clear();
        foreach(var selected in list.Items.Cast<Workflow>().Where(w=>priorIDs.Contains(w.Id)))list.SelectedItems.Add(selected);
        name.Text=current?.Name??"";operations.Text=current is null?"":string.Join(Environment.NewLine,current.Operations);loading=false;Update();
    }
    private void Filter()
    {
        loading=true;list.ItemsSource=workflows.Where(w=>$"{w.Name} {w.Category}".Contains(search.Text,StringComparison.OrdinalIgnoreCase)).OrderBy(w=>w.Name).ToArray();
        foreach(var w in list.Items.Cast<Workflow>().Where(w=>priorIDs.Contains(w.Id)))list.SelectedItems.Add(w);loading=false;
    }
    private void Update()
    {
        if(loading)return;var hasEditor=creating||current is not null;editor.Visibility=hasEditor?Visibility.Visible:Visibility.Collapsed;empty.Visibility=hasEditor?Visibility.Collapsed:Visibility.Visible;
        empty.Text=list.SelectedItems.Count>1?$"{list.SelectedItems.Count} presets selected.\nExport or delete the selection, or select one to edit.":"Select a preset or choose New to create one.";
        var lines=operations.Text.Replace("\r","").Split('\n');var bad=Array.FindIndex(lines,x=>x.Trim().Length>0&&!WorkflowRules.Valid(x.Trim()));
        UI.SetStatusText(validation,bad>=0?$"Line {bad+1}: use -Tag=Value or -Destination<Source.":$"{Arguments.Length} assignments · one per line, without shell quotes",bad>=0?UI.Orange:null);
        save.IsEnabled=hasEditor&&Dirty&&Arguments.Length>0&&bad<0&&!string.IsNullOrWhiteSpace(name.Text);apply.IsEnabled=hasEditor&&Arguments.Length>0&&bad<0&&Targets.Length>0;
        apply.Content=$"Stage for {Targets.Length} {(Targets.Length==1?"file":"files")}";targetCount.Text=$"{Targets.Length} target files · use Save changes in the main window";
        duplicate.IsEnabled=list.SelectedItems.Count==1;delete.IsEnabled=list.SelectedItems.Count>0;
    }
    private void New()
    {
        if(!Resolve())return;loading=true;current=null;creating=true;priorIDs=[];list.SelectedItems.Clear();name.Clear();operations.Clear();loading=false;Update();name.Focus();
    }
    private bool Save()
    {
        try
        {
            var item=new Workflow(current?.Id??Guid.NewGuid().ToString(),name.Text.Trim(),current?.Category??"My Presets",Arguments);WorkflowRules.Validate(item);
            var updated=workflows.Where(w=>w.Id!=item.Id).Append(item).ToArray();store.Save(updated);workflows=updated;current=item;creating=false;priorIDs=[item.Id];loading=true;name.Text=item.Name;loading=false;Filter();status.Text="Preset saved.";Update();return true;
        }
        catch(Exception e){status.Text=e.Message;return false;}
    }
    private void Duplicate()
    {
        if(!Resolve()||current is null)return;var copy=current;loading=true;current=null;creating=true;priorIDs=[];list.SelectedItems.Clear();name.Text=copy.Name+" Copy";operations.Text=string.Join(Environment.NewLine,copy.Operations);loading=false;Update();name.Focus();
    }
    private void Delete()
    {
        var ids=list.SelectedItems.Cast<Workflow>().Select(w=>w.Id).ToHashSet();if(ids.Count==0)return;
        if(UI.Dialog(this,$"Delete {ids.Count} presets? Export a copy first if you want to keep them.","Delete presets",MessageBoxButton.OKCancel,MessageBoxImage.Warning)!=MessageBoxResult.OK)return;
        try{var updated=workflows.Where(w=>!ids.Contains(w.Id)).ToArray();store.Save(updated);workflows=updated;loading=true;current=null;creating=false;priorIDs=[];name.Clear();operations.Clear();loading=false;Filter();Update();status.Text="Presets deleted.";}catch(Exception e){status.Text=e.Message;}
    }
    private void Import()
    {
        if(!Resolve())return;var dialog=new OpenFileDialog{Filter="Preset libraries (*.json)|*.json",Multiselect=true};if(dialog.ShowDialog(this)!=true)return;
        try{var incoming=dialog.FileNames.SelectMany(p=>WorkflowStore.Decode(File.ReadAllText(p))).ToArray();var updated=WorkflowStore.Merge(workflows,incoming);store.Save(updated);var added=updated.Length-workflows.Length;workflows=updated;Filter();Update();status.Text=$"Imported {added} presets; skipped {incoming.Length-added} duplicates.";}catch(Exception e){status.Text="Import failed: "+e.Message;}
    }
    private void Export(bool all)
    {
        if(!Resolve())return;
        var items=all?workflows:list.SelectedItems.Cast<Workflow>().ToArray();if(items.Length==0){status.Text="Select presets to export.";return;}
        var dialog=new SaveFileDialog{Filter="Preset library (*.json)|*.json",FileName="Preset Library.json"};if(dialog.ShowDialog(this)!=true)return;
        try{WorkflowStore.Export(dialog.FileName,items);status.Text=$"Exported {items.Length} presets.";}catch(Exception e){status.Text=e.Message;}
    }
    private void Apply()
    {
        var args=Arguments;var paths=Targets;if(!Resolve())return;allowClose=true;Close();ApplyRequested?.Invoke(args,paths);
    }
}
