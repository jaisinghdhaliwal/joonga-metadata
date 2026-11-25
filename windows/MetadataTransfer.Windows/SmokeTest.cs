using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MetadataTransfer.Core;
namespace MetadataTransfer.Windows;

internal static class SmokeTest
{
    private static string Output=>Path.Combine(AppContext.BaseDirectory,"SmokeResults");
    public static void Failure(Exception e){Directory.CreateDirectory(Output);File.WriteAllText(Path.Combine(Output,"failure.txt"),e.ToString());}
    public static async Task Run(MainWindow main)
    {
        var temp=Path.Combine(Path.GetTempPath(),"MetadataTransfer-smoke-"+Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Output);var originals=Path.Combine(temp,"Originals");var exports=Path.Combine(temp,"Exports");Directory.CreateDirectory(originals);Directory.CreateDirectory(exports);
            var executable=ExifService.Find(null)??throw new FileNotFoundException("Bundled ExifTool is missing.");var exif=new ExifService(executable);
            foreach(var ext in new[]{"jpg","mp4"})
            {
                var name="Sample."+ext;var source=Path.Combine(originals,name);File.Copy(Path.Combine(AppContext.BaseDirectory,"SmokeFixtures","sample."+ext),source);File.Copy(source,Path.Combine(exports,name));
                var result=await exif.WriteAsync(source,["-Make=Sony","-Model=ILCE-7C","-LensModel=Test Lens","-FNumber=2.8"]);if(!result.Success)throw new IOException(result.Output);
            }
            await main.LoadSmokeAsync(originals,exports);await main.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(main,"workspace.png");
            main.ThemeMode=ThemeMode.Light;main.UpdateLayout();Capture(main,"workspace-light.png");main.ThemeMode=ThemeMode.Dark;main.UpdateLayout();Capture(main,"workspace-dark.png");
            await main.CheckMediaWorkspaceAsync(originals);
            await main.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
            Capture(main,"metadata.png");
            var store=new WorkflowStore(Path.Combine(temp,"Workflows.json"));store.Save([new("test","Camera details","My Workflows",["-Make=Sony","-Model=ILCE-7C"])]);
            var workflows=new WorkflowWindow(main,store,new(){{"Selected files",Directory.GetFiles(exports)}},true);workflows.Show();await workflows.Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);Capture(workflows,"workflows.png");workflows.Close();
            File.WriteAllText(Path.Combine(Output,"passed.txt"),"Native Windows window startup, Fluent light/dark layout, synthetic media comparison, shared media table, collapsible inspector, file removal and workflow window rendered. Mica composition, splitter interaction, codecs and WebView2 integration still require manual QA.");
            Application.Current.Shutdown(0);
        }
        catch(Exception e){Failure(e);Application.Current.Shutdown(1);}
        finally {try{Directory.Delete(temp,true);}catch(IOException){}}
    }
    private static void Capture(Window window,string name)
    {
        window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(Output,name));encoder.Save(stream);
    }
}
