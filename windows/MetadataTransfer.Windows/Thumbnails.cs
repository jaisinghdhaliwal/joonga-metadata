using System.IO;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MetadataTransfer.Windows;
internal static class Thumbnails
{
    [StructLayout(LayoutKind.Sequential)] private struct Size { public int Width; public int Height; }
    [ComImport,Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory { [PreserveSig] int GetImage(Size size, uint flags, out IntPtr bitmap); }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode,PreserveSig=true)] private static extern int SHCreateItemFromParsingName(string path,IntPtr context,ref Guid iid,[MarshalAs(UnmanagedType.Interface)] out IImageFactory factory);
    [DllImport("gdi32.dll")] [return:MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr handle);
    private static readonly BlockingCollection<Action> Queue = new();
    private static readonly ConcurrentDictionary<string,Task<ImageSource?>> Cache = new();
    static Thumbnails()
    {
        var worker=new Thread(()=> { foreach(var action in Queue.GetConsumingEnumerable()) action(); }) {IsBackground=true,Name="Shell thumbnails"};worker.SetApartmentState(ApartmentState.STA);worker.Start();
    }
    public static Task<ImageSource?> Load(string path,int size=320)
    {
        var key=$"{path}:{size}:{File.GetLastWriteTimeUtc(path).Ticks}";
        if(Cache.Count > 800) Cache.Clear();
        return Cache.GetOrAdd(key,_=>
        {
            var completion=new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            Queue.Add(()=>
            {
                IImageFactory? factory=null;IntPtr bitmap=IntPtr.Zero;
                try
                {
                    var guid=typeof(IImageFactory).GUID;
                    if(SHCreateItemFromParsingName(path,IntPtr.Zero,ref guid,out factory) >= 0 && factory.GetImage(new Size{Width=size,Height=size},0,out bitmap) >= 0)
                    { var image=Imaging.CreateBitmapSourceFromHBitmap(bitmap,IntPtr.Zero,Int32Rect.Empty,BitmapSizeOptions.FromEmptyOptions());image.Freeze();completion.SetResult(image); }
                    else completion.SetResult(null);
                }
                catch { completion.TrySetResult(null); }
                finally { if(bitmap!=IntPtr.Zero) DeleteObject(bitmap);if(factory is not null) Marshal.ReleaseComObject(factory); }
            });return completion.Task;
        });
    }
}
