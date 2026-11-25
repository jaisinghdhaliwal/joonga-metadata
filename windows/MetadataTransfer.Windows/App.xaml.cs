using System.Windows;
namespace MetadataTransfer.Windows;
public partial class App : Application
{
    internal const string ProductName = "Joonga Metadata";
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--uninstall", StringComparer.OrdinalIgnoreCase))
        {
            if (e.Args.Contains("--quiet", StringComparer.OrdinalIgnoreCase))
            {
                int code = UninstallWindow.RunSilent(e.Args);
                Shutdown(code);
                return;
            }
            var uninstallWindow = new UninstallWindow();
            MainWindow = uninstallWindow;
            uninstallWindow.Show();
            return;
        }
        var smoke=e.Args.Contains("--smoke-test");
        DispatcherUnhandledException += (_, args) =>
        {
            if(smoke){SmokeTest.Failure(args.Exception);Shutdown(1);}
            else MessageBox.Show(args.Exception.Message,ProductName,MessageBoxButton.OK,MessageBoxImage.Error);
            args.Handled=true;
        };
        var window=new MainWindow(smoke);MainWindow=window;
        if(smoke)window.Loaded+=async(_,_)=>await SmokeTest.Run(window);
        window.Show();
    }
}
