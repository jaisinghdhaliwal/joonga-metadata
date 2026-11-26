using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace MetadataTransfer.Windows;

internal static class AppUpdater
{
    private const string Repository = "jaisinghdhaliwal/joonga-metadata";
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(5) };
    private sealed record Release(string Tag, Uri Asset, byte[] Digest, long Size);

    static AppUpdater() => Client.DefaultRequestHeaders.UserAgent.ParseAdd("JoongaMetadata-Updater");

    public static void ShowAbout(Window owner, Func<bool> canInstall)
    {
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
        var window = UI.Window("About " + App.ProductName, 460, 240, owner);
        window.SizeToContent = SizeToContent.Height;
        window.MinHeight = 0;
        window.ResizeMode = ResizeMode.NoResize;
        window.ShowInTaskbar = false;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new Image { Source=owner.Icon, Width=48, Height=48, HorizontalAlignment=HorizontalAlignment.Left });
        panel.Children.Add(UI.Text(App.ProductName, 22, true));
        panel.Children.Add(UI.Muted("Version " + version));
        var status = UI.Text("", 13);
        status.Margin = new Thickness(0, 12, 0, 12);
        panel.Children.Add(status);
        Release? release = null;
        bool running = false;
        var check = UI.Button("Check for updates", () => {});
        var update = UI.Button("Update and restart", () => {});
        update.SetResourceReference(FrameworkElement.StyleProperty, "JoongaAccentButtonStyle");
        update.Visibility = Visibility.Collapsed;
        var close = UI.Button("Done", window.Close);
        close.IsCancel = true;
        window.Closing += (_, e) => { if (running) e.Cancel = true; };
        panel.Children.Add(UI.Horizontal(check, update, close));
        window.Content = panel;

        void SetRunning(bool value)
        {
            running = value;
            check.IsEnabled = update.IsEnabled = close.IsEnabled = !value;
        }
        check.Click += async (_, _) =>
        {
            SetRunning(true);
            release = null;
            update.Visibility = Visibility.Collapsed;
            status.Text = "Checking GitHub…";
            try
            {
                release = await CheckAsync(version);
                status.Text = release is null ? "You’re up to date." : $"Version {release.Tag.TrimStart('v')} is available.";
                update.Visibility = release is null ? Visibility.Collapsed : Visibility.Visible;
            }
            catch (Exception ex) { status.Text = "Update check failed. " + ex.Message; }
            finally { SetRunning(false); }
        };
        update.Click += async (_, _) =>
        {
            if (release is null) return;
            if (!canInstall()) { status.Text = "Save or revert pending edits and wait for current work to finish first."; return; }
            SetRunning(true);
            status.Text = "Downloading and verifying the installer…";
            try
            {
                await InstallAsync(release);
                SetRunning(false);
                window.Close();
                owner.Close();
            }
            catch (Exception ex) { status.Text = "Update could not start. " + ex.Message; }
            finally { SetRunning(false); }
        };
        window.ShowDialog();
    }

    private static async Task<Release?> CheckAsync(string installed)
    {
        using var response = await Client.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest");
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = json.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!tag.StartsWith('v') || !Version.TryParse(tag[1..], out var version) || !Version.TryParse(installed, out var current))
            throw new InvalidDataException("GitHub returned an unsupported version.");
        if (version <= current) return null;
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("This architecture has no published installer.")
        };
        string name = $"Joonga-Metadata-{tag[1..]}-Windows-{architecture}-Setup.exe";
        string expected = $"https://github.com/{Repository}/releases/download/{tag}/{name}";
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != name ||
                asset.GetProperty("browser_download_url").GetString() != expected) continue;
            if (!asset.TryGetProperty("digest", out var property) || property.GetString() is not { Length: 71 } digest ||
                !digest.StartsWith("sha256:", StringComparison.Ordinal))
                throw new InvalidDataException("GitHub has not published a SHA-256 digest for this installer.");
            return new(tag, new Uri(expected), System.Convert.FromHexString(digest[7..]), asset.GetProperty("size").GetInt64());
        }
        throw new InvalidDataException("This release has no installer for your Windows architecture.");
    }

    private static async Task InstallAsync(Release release)
    {
        string directory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        if (!File.Exists(Path.Combine(directory, "Joonga Metadata.dll")))
            throw new IOException("Could not identify the existing app folder.");
        bool allUsers = directory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        bool registeredForAll = IsRegistered(RegistryHive.LocalMachine, directory);
        bool registered = registeredForAll || IsRegistered(RegistryHive.CurrentUser, directory);
        allUsers |= registeredForAll;

        if (release.Size <= 0 || release.Size > 512L * 1024 * 1024)
            throw new InvalidDataException("Unexpected installer size.");
        var staging = Directory.CreateTempSubdirectory("JoongaUpdate-");
        string installer = Path.Combine(staging.FullName, "Joonga Metadata Setup.exe");
        try
        {
            using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var download = await Client.GetAsync(release.Asset, HttpCompletionOption.ResponseHeadersRead, cancellation.Token);
            download.EnsureSuccessStatusCode();
            await using (var stream = await download.Content.ReadAsStreamAsync(cancellation.Token))
            await using (var output = File.Create(installer))
            {
                var buffer = new byte[65536];
                long remaining = release.Size;
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellation.Token)) != 0)
                {
                    remaining -= read;
                    if (remaining < 0) throw new InvalidDataException("Installer exceeds its published size.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                }
                if (remaining != 0) throw new InvalidDataException("Installer download is incomplete.");
            }
            await using (var file = File.OpenRead(installer))
            {
                byte[] actual = await SHA256.HashDataAsync(file, cancellation.Token);
                if (!CryptographicOperations.FixedTimeEquals(actual, release.Digest))
                    throw new InvalidDataException("Installer checksum did not match GitHub.");
            }
            var start = new ProcessStartInfo(installer) { UseShellExecute = true, Verb = allUsers ? "runas" : "" };
            foreach (string argument in new[] { "--install", "--update", "--dir", directory, "--scope",
                allUsers ? "all" : "user", "--start-menu", "false", "--desktop", "false" })
                start.ArgumentList.Add(argument);
            if (!registered) start.ArgumentList.Add("--portable");
            using var setup = Process.Start(start) ?? throw new IOException("Setup could not be launched.");
        }
        catch
        {
            try { staging.Delete(recursive: true); } catch (IOException) { }
            throw;
        }
    }

    private static bool IsRegistered(RegistryHive registryHive, string directory)
    {
        using var hive = RegistryKey.OpenBaseKey(registryHive, RegistryView.Registry64);
        using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\JoongaMetadata");
        return string.Equals((key?.GetValue("InstallLocation") as string)?.TrimEnd('\\'), directory, StringComparison.OrdinalIgnoreCase);
    }
}
