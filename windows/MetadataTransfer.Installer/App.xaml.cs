using System.IO;
using System.Windows;

namespace MetadataTransfer.Installer;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        bool smokeTest = e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);
        bool quiet = e.Args.Contains("--quiet", StringComparer.OrdinalIgnoreCase);
        bool install = e.Args.Contains("--install", StringComparer.OrdinalIgnoreCase);

        DispatcherUnhandledException += (_, args) =>
        {
            if (smokeTest)
            {
                InstallerWindow.WriteSmokeFailure(args.Exception);
                Shutdown(1);
            }
            else if (!quiet)
            {
                MessageBox.Show(args.Exception.Message, $"{InstallEngine.AppName} Setup", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            args.Handled = true;
        };

        if (smokeTest)
        {
            var window = new InstallerWindow(smokeTest: true);
            MainWindow = window;
            window.Loaded += async (_, _) => await window.RunSmokeTestAsync();
            window.Show();
            return;
        }

        if (install)
        {
            var options = ParseInstallOptions(e.Args);
            if (quiet)
            {
                try
                {
                    await InstallEngine.InstallAsync(options, new Progress<InstallProgress>(), CancellationToken.None);
                    Shutdown(0);
                }
                catch
                {
                    Shutdown(1);
                }
                return;
            }

            var autoWindow = new InstallerWindow(autoStartOptions: options);
            MainWindow = autoWindow;
            autoWindow.Show();
            return;
        }

        var main = new InstallerWindow();
        MainWindow = main;
        main.Show();
    }

    private static InstallOptions ParseInstallOptions(string[] args)
    {
        string scope = GetArgValue(args, "--scope") ?? "all";
        bool allUsers = !string.Equals(scope, "user", StringComparison.OrdinalIgnoreCase);
        string defaultDir = allUsers ? InstallEngine.DefaultAllUsersDir : InstallEngine.DefaultCurrentUserDir;
        string dir = GetArgValue(args, "--dir") ?? defaultDir;
        bool startMenu = ParseBoolArg(GetArgValue(args, "--start-menu"), defaultValue: true);
        bool desktop = ParseBoolArg(GetArgValue(args, "--desktop"), defaultValue: false);
        string? userAppData = GetArgValue(args, "--user-appdata");
        string? userDesktop = GetArgValue(args, "--user-desktop");

        return new InstallOptions(
            InstallDirectory: Path.GetFullPath(dir),
            AllUsers: allUsers,
            CreateStartMenuShortcut: startMenu,
            CreateDesktopShortcut: desktop,
            UserAppData: userAppData,
            UserDesktop: userDesktop,
            Update: args.Contains("--update", StringComparer.OrdinalIgnoreCase),
            Register: !args.Contains("--portable", StringComparer.OrdinalIgnoreCase));
    }

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static bool ParseBoolArg(string? value, bool defaultValue)
    {
        if (string.IsNullOrWhiteSpace(value)) return defaultValue;
        return bool.TryParse(value, out bool parsed) ? parsed : defaultValue;
    }
}
