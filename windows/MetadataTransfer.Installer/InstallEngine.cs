using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;
using Microsoft.Win32;

namespace MetadataTransfer.Installer;

internal sealed record InstallOptions(
    string InstallDirectory,
    bool AllUsers,
    bool CreateStartMenuShortcut,
    bool CreateDesktopShortcut,
    string? UserAppData = null,
    string? UserDesktop = null);

internal sealed record InstallProgress(double Fraction, string Status);

internal sealed record ExistingInstall(string InstallLocation, string DisplayVersion, bool AllUsers);

internal static class InstallEngine
{
    public const string AppName = "Joonga Metadata";
    public const string AppVersion = "0.9.2";
    public const string Publisher = "Jai Singh Dhaliwal";
    public const string UninstallKeyName = "JoongaMetadata";
    public const string UninstallSubkey = $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{UninstallKeyName}";
    public const string PayloadResourceName = "JoongaMetadata.Payload.zip";
    public const string InfoUrl = "https://github.com/jaisinghdhaliwal/joonga-metadata";
    public const string ExecutableName = "Joonga Metadata.exe";
    public const string ShortcutFileName = "Joonga Metadata.lnk";

    public static string DefaultAllUsersDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    public static string DefaultCurrentUserDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static ExistingInstall? DetectExistingInstall()
    {
        return TryReadInstallEntry(RegistryHive.LocalMachine, allUsers: true)
            ?? TryReadInstallEntry(RegistryHive.CurrentUser, allUsers: false);
    }

    private static ExistingInstall? TryReadInstallEntry(RegistryHive hive, bool allUsers)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = baseKey.OpenSubKey(UninstallSubkey, writable: false);
            if (key is null) return null;

            string? location = key.GetValue("InstallLocation") as string;
            if (string.IsNullOrWhiteSpace(location)) return null;

            string version = (key.GetValue("DisplayVersion") as string) ?? "";
            return new ExistingInstall(NormalizeDirectory(location), version, allUsers);
        }
        catch
        {
            return null;
        }
    }

    public static (bool Valid, string? NormalizedPath, string? ErrorMessage) ValidateInstallDir(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return (false, null, "Choose a destination folder.");

        string trimmed = rawPath.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            return (false, null, "The destination path contains invalid characters.");

        string full;
        try
        {
            if (!Path.IsPathFullyQualified(trimmed))
                return (false, null, "Enter a full folder path (for example, C:\\Program Files\\Joonga Metadata).");

            full = NormalizeDirectory(trimmed);
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }

        string? root = Path.GetPathRoot(full)?.TrimEnd('\\', '/');
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return (false, null, "Choose a subfolder instead of a drive root.");

        string programFiles = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        string programFilesX86 = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        string windowsDir = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.Windows));

        if (string.Equals(full, programFiles, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, programFilesX86, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(full, windowsDir, StringComparison.OrdinalIgnoreCase) ||
            IsUnderDirectory(full, windowsDir))
        {
            return (false, null, "Choose a dedicated application folder (such as Program Files\\Joonga Metadata).");
        }

        return (true, full, null);
    }

    public static bool RequiresElevation(string installDir, bool allUsers)
    {
        if (allUsers) return true;

        var (valid, full, _) = ValidateInstallDir(installDir);
        if (!valid || full is null) return false;

        string programFiles = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        string programFilesX86 = NormalizeDirectory(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        if (IsUnderDirectory(full, programFiles) || IsUnderDirectory(full, programFilesX86))
            return true;

        return !CanWriteToDirectoryOrAncestor(full);
    }

    public static long GetPayloadUncompressedBytes()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName);
        if (stream is null) return 0;

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        long total = 0;
        foreach (var entry in archive.Entries)
            total += Math.Max(0, entry.Length);
        return total;
    }

    public static long? GetAvailableBytes(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            string full = Path.GetFullPath(path.Trim());
            string? root = Path.GetPathRoot(full);
            if (string.IsNullOrWhiteSpace(root)) return null;
            return new DriveInfo(root).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }

    public static async Task InstallAsync(
        InstallOptions options,
        IProgress<InstallProgress> progress,
        CancellationToken ct)
    {
        var (valid, installDir, error) = ValidateInstallDir(options.InstallDirectory);
        if (!valid || installDir is null)
            throw new InvalidOperationException(error ?? "Invalid installation folder.");

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            progress.Report(new InstallProgress(0.02, "Closing running Joonga Metadata windows…"));
            CloseRunningApplication();

            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(installDir);

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResourceName)
                ?? throw new InvalidOperationException("Installer payload is missing from this build.");
            using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);

            var fileEntries = archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            long totalBytes = Math.Max(1, fileEntries.Sum(e => Math.Max(1, e.Length)));
            long writtenBytes = 0;
            string installPrefix = installDir + Path.DirectorySeparatorChar;

            foreach (var entry in archive.Entries)
            {
                ct.ThrowIfCancellationRequested();
                string relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                string destinationPath = Path.GetFullPath(Path.Combine(installDir, relative));

                if (!destinationPath.StartsWith(installPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(destinationPath, installDir, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Unsafe archive entry path: {entry.FullName}");
                }

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destinationPath);
                    continue;
                }

                string? parent = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);

                entry.ExtractToFile(destinationPath, overwrite: true);
                writtenBytes += Math.Max(1, entry.Length);

                double fraction = 0.05 + 0.85 * Math.Min(1.0, (double)writtenBytes / totalBytes);
                progress.Report(new InstallProgress(fraction, $"Extracting {entry.Name}…"));
            }

            ct.ThrowIfCancellationRequested();
            progress.Report(new InstallProgress(0.92, "Creating shortcuts…"));

            string exePath = Path.Combine(installDir, ExecutableName);
            if (!File.Exists(exePath))
                throw new FileNotFoundException("Extracted installation is incomplete.", exePath);

            if (options.CreateStartMenuShortcut)
            {
                string startMenuFolder = options.AllUsers
                    ? Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
                    : !string.IsNullOrWhiteSpace(options.UserAppData)
                        ? Path.Combine(options.UserAppData, "Microsoft", "Windows", "Start Menu", "Programs")
                        : Environment.GetFolderPath(Environment.SpecialFolder.Programs);

                ShellShortcut.CreateShortcut(
                    Path.Combine(startMenuFolder, ShortcutFileName),
                    exePath,
                    installDir,
                    "Inspect, edit, geotag and transfer photo/video metadata",
                    exePath);
            }

            if (options.CreateDesktopShortcut)
            {
                string desktopFolder = options.AllUsers
                    ? Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
                    : !string.IsNullOrWhiteSpace(options.UserDesktop)
                        ? options.UserDesktop
                        : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);

                ShellShortcut.CreateShortcut(
                    Path.Combine(desktopFolder, ShortcutFileName),
                    exePath,
                    installDir,
                    "Inspect, edit, geotag and transfer photo/video metadata",
                    exePath);
            }

            ct.ThrowIfCancellationRequested();
            progress.Report(new InstallProgress(0.97, "Registering application with Windows…"));
            WriteUninstallRegistryEntry(installDir, exePath, totalBytes, options.AllUsers);

            progress.Report(new InstallProgress(1.0, "Installation complete."));
        }, ct);
    }

    public static async Task UninstallExistingAsync(ExistingInstall existing)
    {
        string installDir = NormalizeDirectory(existing.InstallLocation);
        var (valid, normalized, error) = ValidateInstallDir(installDir);
        if (!valid || normalized is null)
            throw new InvalidOperationException(error ?? "Invalid existing installation path.");

        await Task.Run(() =>
        {
            CloseRunningApplication();

            string[] shortcuts =
            [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), ShortcutFileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), ShortcutFileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), ShortcutFileName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutFileName)
            ];

            foreach (string shortcut in shortcuts)
            {
                try
                {
                    if (File.Exists(shortcut)) File.Delete(shortcut);
                }
                catch
                {
                    // Best effort cleanup.
                }
            }

            var hive = existing.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
            using (var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                baseKey.DeleteSubKeyTree(UninstallSubkey, throwOnMissingSubKey: false);

            if (Directory.Exists(normalized))
                Directory.Delete(normalized, recursive: true);
        });
    }

    private static void WriteUninstallRegistryEntry(string installDir, string exePath, long totalBytes, bool allUsers)
    {
        var hive = allUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser;
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = baseKey.CreateSubKey(UninstallSubkey, writable: true)
            ?? throw new InvalidOperationException("Unable to register Joonga Metadata in Windows Apps & Features.");

        key.SetValue("DisplayName", AppName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", AppVersion, RegistryValueKind.String);
        key.SetValue("Publisher", Publisher, RegistryValueKind.String);
        key.SetValue("DisplayIcon", $"{exePath},0", RegistryValueKind.String);
        key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
        key.SetValue("UninstallString", $"\"{exePath}\" --uninstall", RegistryValueKind.String);
        key.SetValue("QuietUninstallString", $"\"{exePath}\" --uninstall --quiet", RegistryValueKind.String);
        key.SetValue("EstimatedSize", (int)Math.Clamp(totalBytes / 1024, 1, int.MaxValue), RegistryValueKind.DWord);
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("URLInfoAbout", InfoUrl, RegistryValueKind.String);
    }

    private static void CloseRunningApplication()
    {
        foreach (var proc in Process.GetProcessesByName("Joonga Metadata"))
        {
            using (proc)
            {
                try
                {
                    if (proc.CloseMainWindow())
                        proc.WaitForExit(3000);
                    if (!proc.HasExited)
                    {
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(3000);
                    }
                }
                catch
                {
                    // Ignore processes that already exited.
                }
            }
        }
    }

    private static bool CanWriteToDirectoryOrAncestor(string fullPath)
    {
        try
        {
            string? current = fullPath;
            while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
                current = Path.GetDirectoryName(current);

            if (string.IsNullOrEmpty(current)) return false;

            string probe = Path.Combine(current, $".joonga-write-probe-{Guid.NewGuid():N}");
            using (File.Create(probe)) { }
            File.Delete(probe);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUnderDirectory(string candidate, string parent)
    {
        if (string.IsNullOrWhiteSpace(parent)) return false;
        string c = NormalizeDirectory(candidate) + Path.DirectorySeparatorChar;
        string p = NormalizeDirectory(parent) + Path.DirectorySeparatorChar;
        return c.StartsWith(p, StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeDirectory(string path) =>
        Path.GetFullPath(path).TrimEnd('\\', '/');
}
