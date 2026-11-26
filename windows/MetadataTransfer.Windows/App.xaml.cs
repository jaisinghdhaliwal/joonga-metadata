using System.Windows;
using MetadataTransfer.Core;
namespace MetadataTransfer.Windows;
public partial class App : Application
{
    internal const string ProductName = "Joonga Tag";
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
            else UI.Dialog(Application.Current?.MainWindow, args.Exception.Message,ProductName,MessageBoxButton.OK,MessageBoxImage.Error);
            args.Handled=true;
        };
        if (!smoke)
        {
            try { Settings.MigrateLegacyData(); }
            catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException)
            {
                UI.Dialog(null, error.Message, "Could not copy previous settings or workflows");
            }
        }
        var window=new MainWindow(smoke);MainWindow=window;
        if(smoke)window.Loaded+=async(_,_)=>await SmokeTest.Run(window);
        window.Show();
    }
}
