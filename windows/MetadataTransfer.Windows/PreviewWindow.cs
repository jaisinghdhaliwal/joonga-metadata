using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MetadataTransfer.Core;
using System.IO;
namespace MetadataTransfer.Windows;
internal sealed class PreviewWindow : Window
{
    private readonly string[] paths;private int index;private int generation;
    private readonly Grid display=new();private readonly TextBlock caption=UI.Text("",14,true);private MediaElement? player;
    public PreviewWindow(string[] files,Window owner)
    {
        paths=files;Title="Quick preview";Width=980;Height=720;Owner=owner;ThemeMode=ThemeMode.System;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var dock=new DockPanel{Margin=new Thickness(16)};
        var controls=UI.Horizontal(UI.Button("‹",()=>Move(-1),"Previous · Left arrow"),UI.Button("›",()=>Move(1),"Next · Right arrow"),UI.Button("Play",()=>player?.Play()),UI.Button("Pause",()=>player?.Pause()),UI.Button("Open externally",()=>UI.Open(paths[index])),UI.Button("Close",Close));
        DockPanel.SetDock(controls,Dock.Bottom);dock.Children.Add(controls);caption.Margin=new Thickness(8,0,8,12);DockPanel.SetDock(caption,Dock.Top);dock.Children.Add(caption);dock.Children.Add(display);Content=dock;
        PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Escape or Key.Space){Close();e.Handled=true;}else if(e.Key==Key.Left)Move(-1);else if(e.Key==Key.Right)Move(1);};Loaded+=async(_,_)=>await ShowFile();Closed+=(_,_)=>player?.Close();
    }
    private async void Move(int direction){index=Math.Clamp(index+direction,0,paths.Length-1);await ShowFile();}
    private async Task ShowFile()
    {
        var version=++generation;player?.Close();player=null;display.Children.Clear();caption.Text=$"{Path.GetFileName(paths[index])}  ·  {index+1} of {paths.Length}";
        var thumbnail=await Thumbnails.Load(paths[index],1600);if(version!=generation)return;
        if(Matching.Kind(paths[index])=="video")
        {
            player=new MediaElement{Source=new Uri(paths[index]),LoadedBehavior=MediaState.Manual,UnloadedBehavior=MediaState.Close,Stretch=Stretch.Uniform};
            player.MediaFailed+=(_,_)=>{display.Children.Clear();var fallback=new StackPanel();fallback.Children.Add(new Image{Source=thumbnail,MaxHeight=420});fallback.Children.Add(UI.Muted("Windows cannot decode this file. Use Open externally to view it in your preferred player."));display.Children.Add(fallback);};display.Children.Add(player);player.Play();
        }
        else display.Children.Add(new Image{Source=thumbnail,Stretch=Stretch.Uniform});
    }
}
