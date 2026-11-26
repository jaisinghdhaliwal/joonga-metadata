using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;
using MetadataTransfer.Core;

namespace MetadataTransfer.Windows;

internal sealed class UninstallWindow : Window
{
    private const string UninstallSubkey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\JoongaTag";
    private const string ShortcutFileName = "Joonga Tag.lnk";

    private readonly string installDir;
    private readonly CheckBox removeUserData;
    private readonly Button uninstallButton;
    private readonly Button cancelButton;
    private readonly TextBlock statusText;

    public UninstallWindow(bool fromSettings = false)
    {
        installDir = NormalizeDirectory(AppContext.BaseDirectory);
        Title = $"Uninstall {App.ProductName}";
        Width = 500;
        Height = 360;
        MinWidth = 460;
        MinHeight = 330;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ThemeMode = ThemeMode.System;
        FontFamily = new FontFamily("Segoe UI Variable Text");
        FontSize = 12;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico"));

        var chromeRow = CreateCaptionRow();
        var root = new DockPanel();
        DockPanel.SetDock(chromeRow, Dock.Top);
        root.Children.Add(chromeRow);

        var body = new DockPanel { Margin = new Thickness(24, 12, 24, 20) };

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 16) };
        header.Children.Add(new Image
        {
            Source = Icon,
            Width = 44,
            Height = 44,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0)
        });
        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(UI.Text($"Uninstall {App.ProductName}", 20, true));
        titles.Children.Add(UI.Muted("Remove Joonga Tag and its shortcuts from this PC", 12));
        header.Children.Add(titles);
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);

        cancelButton = UI.Button("Cancel", () => { if (fromSettings) Close(); else Application.Current.Shutdown(0); });
        cancelButton.IsCancel = true;
        cancelButton.MinWidth = 96;

        uninstallButton = UI.Button("Uninstall", () => _ = BeginUninstallAsync());
        uninstallButton.MinWidth = 112;
        uninstallButton.SetResourceReference(StyleProperty, "JoongaAccentButtonStyle");

        statusText = UI.Muted("");
        statusText.VerticalAlignment = VerticalAlignment.Center;

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        var buttons = UI.Horizontal(cancelButton, uninstallButton);
        DockPanel.SetDock(buttons, Dock.Right);
        footer.Children.Add(buttons);
        footer.Children.Add(statusText);
        DockPanel.SetDock(footer, Dock.Bottom);
        body.Children.Add(footer);

        var cards = new StackPanel();
        var locationCard = new StackPanel();
        locationCard.Children.Add(UI.Text("Installation folder", 13, true));
        var pathText = UI.Muted(installDir, 12);
        pathText.Margin = new Thickness(0, 4, 0, 0);
        locationCard.Children.Add(pathText);
        var locBorder = UI.Card(locationCard);
        locBorder.Margin = new Thickness(0, 0, 0, 10);
        cards.Children.Add(locBorder);

        removeUserData = new CheckBox
        {
            Content = "Also remove saved workflows and app settings",
            IsChecked = fromSettings,
            IsEnabled = !fromSettings
        };
        var dataSection = new StackPanel();
        dataSection.Children.Add(removeUserData);
        var dataHint = UI.Muted("Leaves your photo and video files untouched.", 11);
        dataHint.Margin = new Thickness(24, 4, 0, 0);
        dataSection.Children.Add(dataHint);
        cards.Children.Add(UI.Card(dataSection));

        body.Children.Add(cards);
        root.Children.Add(body);
        Content = root;

        ApplyWindowChrome(chromeRow);
    }

    private Border CreateCaptionRow()
    {
        var icon = new Image
        {
            Source = Icon,
            Width = 16,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 8, 0)
        };
        var title = UI.Text(Title, 12);
        title.TextWrapping = TextWrapping.NoWrap;
        var row = UI.Horizontal(icon, title);
        row.Margin = new Thickness(0, 0, 110, 0);
        return new Border
        {
            Height = 36,
            Child = row
        };
    }

    private void ApplyWindowChrome(Border chromeRow)
    {
        bool integrated = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemParameters.HighContrast;
        if (!integrated)
        {
            chromeRow.Visibility = Visibility.Collapsed;
            WindowChrome.SetWindowChrome(this, null);
            return;
        }

        var resize = SystemParameters.WindowResizeBorderThickness;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = Math.Max(0, 36 - resize.Top),
            ResizeBorderThickness = resize,
            NonClientFrameEdges = NonClientFrameEdges.Left | NonClientFrameEdges.Right | NonClientFrameEdges.Bottom,
            GlassFrameThickness = WindowChrome.GlassFrameCompleteThickness,
            UseAeroCaptionButtons = true
        });
    }

    private async Task BeginUninstallAsync()
    {
        bool deleteUserData = removeUserData.IsChecked == true;
        uninstallButton.IsEnabled = false;
        cancelButton.IsEnabled = false;
        removeUserData.IsEnabled = false;
        statusText.Text = "Removing Joonga Tag…";

        try
        {
            string userAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string userLocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            if (RequiresElevation(installDir) && !IsElevated())
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    throw new InvalidOperationException("Unable to locate the application executable.");

                var args = new List<string> { "--uninstall", "--quiet" };
                if (deleteUserData)
                {
                    args.Add("--remove-data");
                    args.Add("--user-appdata");
                    args.Add(QuoteArgument(userAppData));
                    args.Add("--user-localappdata");
                    args.Add(QuoteArgument(userLocalAppData));
                }

                var psi = new ProcessStartInfo(exePath)
                {
                    Arguments = string.Join(" ", args),
                    UseShellExecute = true,
                    Verb = "runas"
                };

                using var child = Process.Start(psi) ?? throw new InvalidOperationException("Could not start elevated uninstaller.");
                await child.WaitForExitAsync();
                if (child.ExitCode != 0)
                    throw new InvalidOperationException($"Uninstall exited with code {child.ExitCode}.");

                Application.Current.Shutdown(0);
                return;
            }

            await Task.Run(() => ExecuteUninstall(installDir, userAppData));
            ScheduleRemoval(installDir, deleteUserData, userAppData, userLocalAppData);
            Application.Current.Shutdown(0);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            statusText.Text = "Administrator permission was cancelled.";
            uninstallButton.IsEnabled = true;
            cancelButton.IsEnabled = true;
            removeUserData.IsEnabled = true;
        }
        catch (Exception ex)
        {
            statusText.Text = ex.Message;
            uninstallButton.IsEnabled = true;
            cancelButton.IsEnabled = true;
            removeUserData.IsEnabled = true;
        }
    }

    public static int RunSilent(string[] args)
    {
        string installDir = NormalizeDirectory(AppContext.BaseDirectory);
        bool deleteUserData = args.Contains("--remove-data", StringComparer.OrdinalIgnoreCase);
        string userAppData = GetArgValue(args, "--user-appdata")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string userLocalAppData = GetArgValue(args, "--user-localappdata")
            ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        if (RequiresElevation(installDir) && !IsElevated())
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return 1;

            var childArgs = new List<string> { "--uninstall", "--quiet" };
            if (deleteUserData)
            {
                childArgs.Add("--remove-data");
                childArgs.Add("--user-appdata");
                childArgs.Add(QuoteArgument(userAppData));
                childArgs.Add("--user-localappdata");
                childArgs.Add(QuoteArgument(userLocalAppData));
            }

            try
            {
                var psi = new ProcessStartInfo(exePath)
                {
                    Arguments = string.Join(" ", childArgs),
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using var child = Process.Start(psi);
                if (child is null) return 1;
                child.WaitForExit();
                return child.ExitCode;
            }
            catch (Win32Exception)
            {
                return 1;
            }
        }

        ExecuteUninstall(installDir, userAppData);
        ScheduleRemoval(installDir, deleteUserData, userAppData, userLocalAppData);
        return 0;
    }

    private static void ExecuteUninstall(string installDir, string userAppData)
    {
        ValidateSafeUninstallDirectory(installDir);
        RemoveMatchingShortcuts(installDir, userAppData);
        RemoveUninstallRegistryEntries(installDir);

    }

    private static void ValidateSafeUninstallDirectory(string installDir)
    {
        if (string.IsNullOrWhiteSpace(installDir))
            throw new InvalidOperationException("Invalid installation directory.");

        string full = Path.GetFullPath(installDir);
        string? root = Path.GetPathRoot(full);
        if (string.Equals(full, root?.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to uninstall from a drive root.");

        string programFiles = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        string programFilesX86 = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        if (string.Equals(full, programFiles, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, programFilesX86, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to uninstall from Program Files root.");

        if (!File.Exists(Path.Combine(full, "Joonga Tag.exe")))
            throw new InvalidOperationException("Joonga Tag executable was not found in this directory.");
        _ = ReadInstalledFiles(full);
    }


    private static void RemoveMatchingShortcuts(string installDir, string userAppData)
    {
        string exePath = Path.Combine(installDir, "Joonga Tag.exe");
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ShortcutFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ShortcutFileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutFileName)
        };

        if (!string.IsNullOrWhiteSpace(userAppData))
        {
            candidates.Add(Path.Combine(userAppData, "Microsoft", "Windows", "Start Menu", "Programs", ShortcutFileName));
        }

        foreach (string shortcut in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(shortcut)) continue;
                string? target = TryReadShortcutTarget(shortcut);
                if (target is null || string.Equals(Path.GetFullPath(target), exePath, StringComparison.OrdinalIgnoreCase))
                    File.Delete(shortcut);
            }
            catch
            {
                // Best effort shortcut cleanup.
            }
        }
    }

    private static void RemoveUninstallRegistryEntries(string installDir)
    {
        if (IsElevated())
            TryDeleteRegistryKeyIfMatches(RegistryHive.LocalMachine, installDir);
        TryDeleteRegistryKeyIfMatches(RegistryHive.CurrentUser, installDir);
    }

    private static void TryDeleteRegistryKeyIfMatches(RegistryHive hive, string installDir)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(UninstallSubkey, writable: false);
            if (key is null) return;

            var recordedLocation = key.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(recordedLocation) &&
                !string.Equals(NormalizeDirectory(recordedLocation), installDir, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            baseKey.DeleteSubKeyTree(UninstallSubkey, throwOnMissingSubKey: false);
        }
        catch
        {
            // Best effort registry cleanup.
        }
    }

    private const string FileManifest = "JoongaTag.install-files.txt";

    private static string[] ReadInstalledFiles(string directory)
    {
        string manifest = Path.Combine(directory, FileManifest);
        if (!File.Exists(manifest))
            throw new InvalidOperationException("The installation file list is missing. Use an official Joonga Tag package.");
        string prefix = directory + Path.DirectorySeparatorChar;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { manifest };
        foreach (string entry in File.ReadLines(manifest))
        {
            if (string.IsNullOrWhiteSpace(entry) || Path.IsPathRooted(entry) || entry.Contains(':'))
                throw new InvalidDataException("The installation file list contains an invalid path.");
            string target = Path.GetFullPath(Path.Combine(directory, entry));
            if (!target.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installation file list points outside Joonga Tag.");
            for (string? folder = Path.GetDirectoryName(target); folder is not null && folder.StartsWith(directory, StringComparison.OrdinalIgnoreCase); folder = Path.GetDirectoryName(folder))
                if (Directory.Exists(folder) && File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))
                    throw new IOException("An installation directory redirects to another location.");
            files.Add(target);
        }
        if (!files.Contains(Path.Combine(directory, "Joonga Tag.exe")))
            throw new InvalidDataException("The installation file list is incomplete.");
        return files.ToArray();
    }

    private static void ScheduleRemoval(string installDir, bool removeData, string userAppData, string userLocalAppData)
    {
        var files = ReadInstalledFiles(installDir);
        string[] data = removeData ? [Path.Combine(userAppData, Settings.ProductFolder),
            Path.Combine(userLocalAppData, Settings.ProductFolder)] : [];
        string payload = Convert.ToBase64String(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            files, data, installDir, pid = Environment.ProcessId
        }));
        string script = """
            $job = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String([Console]::In.ReadToEnd())) | ConvertFrom-Json
            Wait-Process -Id $job.pid -Timeout 30 -ErrorAction SilentlyContinue
            if (Get-Process -Id $job.pid -ErrorAction SilentlyContinue) { exit 1 }
            $dirs = @{}
            $failed = $false
            foreach ($file in $job.files) {
                for ($i = 0; $i -lt 10; $i++) {
                    try { [IO.File]::Delete($file); break } catch { Start-Sleep -Milliseconds 350 }
                }
                if ([IO.File]::Exists($file)) { $failed = $true }
                $dir = [IO.Path]::GetDirectoryName($file)
                while ($dir -eq $job.installDir -or $dir.StartsWith($job.installDir + '\', [StringComparison]::OrdinalIgnoreCase)) {
                    $dirs[$dir] = 1
                    $dir = [IO.Path]::GetDirectoryName($dir)
                }
            }
            foreach ($dir in ($dirs.Keys | Sort-Object Length -Descending)) {
                try { if ([IO.Directory]::Exists($dir)) { [IO.Directory]::Delete($dir, $false) } } catch { }
            }
            foreach ($dir in $job.data) {
                try { if ([IO.Directory]::Exists($dir)) { [IO.Directory]::Delete($dir, $true) } }
                catch { $failed = $true }
            }
            if ($failed) {
                Add-Type -AssemblyName PresentationFramework
                [System.Windows.MessageBox]::Show('Some Joonga Tag files could not be removed. Close any other copies of the app before uninstalling.', 'Joonga Tag') | Out-Null
            }
            """;
        var psi = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardInput = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System)
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var helper = Process.Start(psi) ?? throw new IOException("Could not start uninstall cleanup.");
        helper.StandardInput.Write(payload);
        helper.StandardInput.Close();
    }

    private static bool RequiresElevation(string installDir)
    {
        string programFiles = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        string programFilesX86 = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        if (IsUnderDirectory(installDir, programFiles) || IsUnderDirectory(installDir, programFilesX86))
            return true;

        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(UninstallSubkey, writable: false);
            if (key?.GetValue("InstallLocation") is string loc &&
                string.Equals(NormalizeDirectory(loc), installDir, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        catch
        {
            // Ignore registry probe failures.
        }

        try
        {
            string probe = Path.Combine(installDir, $".uninstall-probe-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static bool IsUnderDirectory(string candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent)) return false;
        string c = NormalizeDirectory(candidate) + Path.DirectorySeparatorChar;
        string p = NormalizeDirectory(parent) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd('\\', '/');

    private static string? GetArgValue(string[] args, string flag)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }

    private static string QuoteArgument(string value) =>
        $"\"{value.TrimEnd('\\').Replace("\"", "\\\"")}\"";

    private static string? TryReadShortcutTarget(string shortcutPath)
    {
        IShellLinkW? link = null;
        try
        {
            link = (IShellLinkW)(object)new ShellLink();
            ((IPersistFile)link).Load(shortcutPath, 0);
            var sb = new StringBuilder(1024);
            link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
            string target = sb.ToString();
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (link is not null && Marshal.IsComObject(link))
                Marshal.ReleaseComObject(link);
        }
    }

    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private sealed class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out ushort pwHotkey);
        void SetHotkey(ushort wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }
}
