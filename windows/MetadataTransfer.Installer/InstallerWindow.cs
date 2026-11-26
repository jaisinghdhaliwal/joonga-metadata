using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using Microsoft.Win32;

namespace MetadataTransfer.Installer;

internal sealed class InstallerWindow : Window
{
    private static readonly Brush SubtleBrush = new SolidColorBrush(Color.FromArgb(14, 128, 128, 128));
    private static readonly Brush GreenBrush = new SolidColorBrush(Color.FromRgb(51, 160, 107));
    private static readonly Brush ErrorBrush = new SolidColorBrush(Color.FromRgb(209, 67, 52));
    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");

    private readonly ContentPresenter stageHost = new();
    private readonly Border chromeRow;
    private readonly long payloadBytes;
    private ExistingInstall? existingInstall;

    // Configure view state
    private RadioButton? allUsersRadio;
    private RadioButton? currentUserRadio;
    private TextBox? folderBox;
    private TextBlock? diskSpaceText;
    private TextBlock? configureErrorText;
    private CheckBox? startMenuCheck;
    private CheckBox? desktopCheck;
    private Button? installButton;
    private Button? uninstallExistingButton;
    private bool customPathEdited;
    private bool updatingPathProgrammatically;

    // Installing view state
    private ProgressBar? progressBar;
    private TextBlock? progressPercentText;
    private TextBlock? progressStatusText;
    private CancellationTokenSource? installCts;

    // Complete view state
    private string installedDir = "";
    private CheckBox? launchCheck;

    private readonly InstallOptions? autoStartOptions;

    public InstallerWindow(bool smokeTest = false, InstallOptions? autoStartOptions = null)
    {
        this.autoStartOptions = autoStartOptions;
        payloadBytes = InstallEngine.GetPayloadUncompressedBytes();
        existingInstall = smokeTest ? null : InstallEngine.DetectExistingInstall();

        Title = $"{InstallEngine.AppName} Setup";
        Width = 560;
        Height = 540;
        MinWidth = 520;
        MinHeight = 500;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ThemeMode = ThemeMode.System;
        FontFamily = new FontFamily("Segoe UI Variable Text");
        FontSize = 12;
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico"));

        chromeRow = CreateCaptionRow();
        var root = new DockPanel();
        DockPanel.SetDock(chromeRow, Dock.Top);
        root.Children.Add(chromeRow);

        var body = new DockPanel { Margin = new Thickness(26, 10, 26, 22) };
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        body.Children.Add(header);
        body.Children.Add(stageHost);

        root.Children.Add(body);
        Content = root;

        ApplyWindowChrome();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;

        if (autoStartOptions is not null)
        {
            Loaded += async (_, _) => await RunInstallViewAsync(autoStartOptions);
        }
        else
        {
            ShowConfigureView();
        }
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
        var title = Text(Title, 12);
        title.TextWrapping = TextWrapping.NoWrap;
        var row = Horizontal(icon, title);
        row.Margin = new Thickness(0, 0, 110, 0);
        return new Border { Height = 36, Child = row };
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.InvokeAsync(ApplyWindowChrome);
    }

    private void ApplyWindowChrome()
    {
        bool integrated = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemParameters.HighContrast;
        chromeRow.Visibility = integrated ? Visibility.Visible : Visibility.Collapsed;
        if (!integrated)
        {
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

    private UIElement BuildHeader()
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var icon = new Image
        {
            Source = Icon,
            Width = 48,
            Height = 48,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0)
        };
        DockPanel.SetDock(icon, Dock.Left);
        row.Children.Add(icon);

        if (existingInstall is not null)
        {
            string badgeLabel = string.IsNullOrWhiteSpace(existingInstall.DisplayVersion)
                ? "Installed"
                : $"Installed: v{existingInstall.DisplayVersion}";
            var badge = new Border
            {
                Background = SubtleBrush,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 4, 10, 4),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Muted(badgeLabel, 11)
            };
            DockPanel.SetDock(badge, Dock.Right);
            row.Children.Add(badge);
        }

        var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        titles.Children.Add(Text(InstallEngine.AppName, 22, bold: true));
        var subtitle = Muted($"Version {InstallEngine.AppVersion}  •  Photo & video metadata editor", 12);
        subtitle.Margin = new Thickness(0, 2, 0, 0);
        titles.Children.Add(subtitle);
        row.Children.Add(titles);
        return row;
    }

    private void ShowConfigureView()
    {
        bool initialAllUsers = existingInstall?.AllUsers ?? true;
        string initialDir = existingInstall?.InstallLocation
            ?? (initialAllUsers ? InstallEngine.DefaultAllUsersDir : InstallEngine.DefaultCurrentUserDir);

        var dock = new DockPanel();

        // Footer
        configureErrorText = Text("", 11.5);
        configureErrorText.Foreground = ErrorBrush;
        configureErrorText.Margin = new Thickness(0, 0, 0, 8);
        configureErrorText.Visibility = Visibility.Collapsed;

        var cancelButton = Button("Cancel", () => Application.Current.Shutdown(0));
        cancelButton.IsCancel = true;
        cancelButton.MinWidth = 92;

        installButton = new Button
        {
            MinWidth = 122,
            MinHeight = 32,
            Padding = new Thickness(14, 6, 14, 6),
            Margin = new Thickness(4, 0, 0, 0)
        };
        installButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        installButton.SetResourceReference(Control.BackgroundProperty, SystemColors.HighlightBrushKey);
        installButton.SetResourceReference(Control.ForegroundProperty, SystemColors.HighlightTextBrushKey);
        installButton.Click += (_, _) => _ = HandleInstallClickedAsync();

        var footerRight = Horizontal(cancelButton, installButton);
        DockPanel.SetDock(footerRight, Dock.Right);

        var footerBar = new DockPanel();
        if (existingInstall is not null)
        {
            uninstallExistingButton = Button("Uninstall…", () => _ = HandleUninstallExistingAsync());
            DockPanel.SetDock(uninstallExistingButton, Dock.Left);
            footerBar.Children.Add(uninstallExistingButton);
        }
        footerBar.Children.Add(footerRight);
        footerBar.Children.Add(new Border());

        var footerStack = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        footerStack.Children.Add(configureErrorText);
        footerStack.Children.Add(footerBar);
        DockPanel.SetDock(footerStack, Dock.Bottom);
        dock.Children.Add(footerStack);

        // Cards
        var cards = new StackPanel();

        // Card 1: Scope
        allUsersRadio = new RadioButton
        {
            Content = "Install for all users (recommended)",
            IsChecked = initialAllUsers,
            GroupName = "InstallScope",
            Margin = new Thickness(0, 6, 0, 4)
        };
        currentUserRadio = new RadioButton
        {
            Content = "Install for current user only",
            IsChecked = !initialAllUsers,
            GroupName = "InstallScope",
            Margin = new Thickness(0, 2, 0, 0)
        };
        allUsersRadio.Checked += (_, _) => OnScopeChanged(allUsers: true);
        currentUserRadio.Checked += (_, _) => OnScopeChanged(allUsers: false);

        var scopeSection = new StackPanel();
        scopeSection.Children.Add(Text("Installation scope", 13, bold: true));
        scopeSection.Children.Add(allUsersRadio);
        scopeSection.Children.Add(currentUserRadio);
        var scopeCard = Card(scopeSection);
        scopeCard.Margin = new Thickness(0, 0, 0, 10);
        cards.Children.Add(scopeCard);

        // Card 2: Destination folder
        folderBox = new TextBox
        {
            Text = initialDir,
            MinHeight = 32,
            Padding = new Thickness(8, 5, 8, 5),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(folderBox, "Destination folder");
        folderBox.TextChanged += (_, _) =>
        {
            if (!updatingPathProgrammatically) customPathEdited = true;
            UpdateConfigureState();
        };

        var browseButton = Button("Browse…", BrowseForFolder);
        browseButton.Margin = new Thickness(8, 0, 0, 0);
        browseButton.MinWidth = 92;

        var pathRow = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
        DockPanel.SetDock(browseButton, Dock.Right);
        pathRow.Children.Add(browseButton);
        pathRow.Children.Add(folderBox);

        diskSpaceText = Muted("", 11);
        var destSection = new StackPanel();
        destSection.Children.Add(Text("Destination folder", 13, bold: true));
        destSection.Children.Add(pathRow);
        destSection.Children.Add(diskSpaceText);
        var destCard = Card(destSection);
        destCard.Margin = new Thickness(0, 0, 0, 10);
        cards.Children.Add(destCard);

        // Card 3: Shortcuts
        startMenuCheck = new CheckBox
        {
            Content = "Create Start Menu shortcut",
            IsChecked = true,
            Margin = new Thickness(0, 6, 0, 4)
        };
        desktopCheck = new CheckBox
        {
            Content = "Create Desktop shortcut",
            IsChecked = false,
            Margin = new Thickness(0, 2, 0, 0)
        };
        var shortcutSection = new StackPanel();
        shortcutSection.Children.Add(Text("Shortcuts", 13, bold: true));
        shortcutSection.Children.Add(startMenuCheck);
        shortcutSection.Children.Add(desktopCheck);
        cards.Children.Add(Card(shortcutSection));

        dock.Children.Add(new ScrollViewer
        {
            Content = cards,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        });

        stageHost.Content = dock;
        UpdateConfigureState();
    }

    private void OnScopeChanged(bool allUsers)
    {
        if (folderBox is null) return;

        string current = folderBox.Text.Trim();
        bool matchesKnownDefault =
            string.Equals(current, InstallEngine.DefaultAllUsersDir, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(current, InstallEngine.DefaultCurrentUserDir, StringComparison.OrdinalIgnoreCase);

        if (!customPathEdited || matchesKnownDefault)
        {
            updatingPathProgrammatically = true;
            folderBox.Text = allUsers ? InstallEngine.DefaultAllUsersDir : InstallEngine.DefaultCurrentUserDir;
            updatingPathProgrammatically = false;
            customPathEdited = false;
        }

        UpdateConfigureState();
    }

    private void BrowseForFolder()
    {
        if (folderBox is null) return;
        var picker = new OpenFolderDialog { Title = $"Choose where to install {InstallEngine.AppName}" };
        string current = folderBox.Text.Trim();
        if (Directory.Exists(current))
        {
            picker.InitialDirectory = current;
        }
        else
        {
            string? parent = Path.GetDirectoryName(current);
            if (!string.IsNullOrWhiteSpace(parent) && Directory.Exists(parent))
                picker.InitialDirectory = parent;
        }

        if (picker.ShowDialog(this) != true) return;

        string chosen = InstallEngine.NormalizeDirectory(picker.FolderName);
        if (!string.Equals(Path.GetFileName(chosen), InstallEngine.AppName, StringComparison.OrdinalIgnoreCase))
            chosen = Path.Combine(chosen, InstallEngine.AppName);

        updatingPathProgrammatically = true;
        folderBox.Text = chosen;
        updatingPathProgrammatically = false;
        customPathEdited = true;
        UpdateConfigureState();
    }

    private void UpdateConfigureState()
    {
        if (folderBox is null || diskSpaceText is null || installButton is null || configureErrorText is null)
            return;

        bool allUsers = allUsersRadio?.IsChecked == true;
        string path = folderBox.Text;
        var (valid, normalized, error) = InstallEngine.ValidateInstallDir(path);

        long requiredBytes = payloadBytes > 0 ? payloadBytes : 120L * 1024 * 1024;
        long? availableBytes = normalized is not null ? InstallEngine.GetAvailableBytes(normalized) : null;
        string reqText = payloadBytes > 0
            ? $"Space required: {FormatBytes(requiredBytes)}"
            : $"Estimated space required: ~{FormatBytes(requiredBytes)}";
        string availText = availableBytes is long avail
            ? $"  •  Available: {FormatBytes(avail)}"
            : "";
        diskSpaceText.Text = reqText + availText;

        bool enoughSpace = availableBytes is null || availableBytes.Value >= requiredBytes;
        if (!valid)
        {
            configureErrorText.Text = error ?? "Invalid destination folder.";
            configureErrorText.Visibility = Visibility.Visible;
        }
        else if (!enoughSpace)
        {
            configureErrorText.Text = "Not enough free disk space on the selected drive.";
            configureErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            configureErrorText.Visibility = Visibility.Collapsed;
            configureErrorText.Text = "";
        }

        installButton.IsEnabled = valid && enoughSpace;
        string actionLabel = existingInstall is not null ? "Update" : "Install";
        bool needsShield = valid && InstallEngine.RequiresElevation(normalized!, allUsers) && !InstallEngine.IsElevated();
        installButton.Content = needsShield ? BuildShieldButtonContent(actionLabel) : actionLabel;
        AutomationProperties.SetName(installButton, actionLabel);
    }

    private static UIElement BuildShieldButtonContent(string label)
    {
        var shield = new TextBlock
        {
            Text = "\uE72E",
            FontFamily = IconFont,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center
        };
        return Horizontal(shield, text);
    }

    private async Task HandleInstallClickedAsync()
    {
        if (folderBox is null || configureErrorText is null) return;

        bool allUsers = allUsersRadio?.IsChecked == true;
        var (valid, normalized, error) = InstallEngine.ValidateInstallDir(folderBox.Text);
        if (!valid || normalized is null)
        {
            configureErrorText.Text = error ?? "Invalid destination folder.";
            configureErrorText.Visibility = Visibility.Visible;
            return;
        }

        var options = new InstallOptions(
            InstallDirectory: normalized,
            AllUsers: allUsers,
            CreateStartMenuShortcut: startMenuCheck?.IsChecked == true,
            CreateDesktopShortcut: desktopCheck?.IsChecked == true,
            UserAppData: Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            UserDesktop: Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

        if (InstallEngine.RequiresElevation(normalized, allUsers) && !InstallEngine.IsElevated())
        {
            try
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                    throw new InvalidOperationException("Unable to locate the setup executable.");

                var args = new List<string>
                {
                    "--install",
                    "--dir", QuoteArg(options.InstallDirectory),
                    "--scope", options.AllUsers ? "all" : "user",
                    "--start-menu", options.CreateStartMenuShortcut ? "true" : "false",
                    "--desktop", options.CreateDesktopShortcut ? "true" : "false",
                    "--user-appdata", QuoteArg(options.UserAppData ?? ""),
                    "--user-desktop", QuoteArg(options.UserDesktop ?? "")
                };

                var psi = new ProcessStartInfo(exePath)
                {
                    Arguments = string.Join(" ", args),
                    UseShellExecute = true,
                    Verb = "runas"
                };

                Process.Start(psi);
                Application.Current.Shutdown(0);
                return;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                configureErrorText.Text = "Administrator permission was cancelled. You can choose “Install for current user only” instead.";
                configureErrorText.Visibility = Visibility.Visible;
                return;
            }
            catch (Exception ex)
            {
                configureErrorText.Text = ex.Message;
                configureErrorText.Visibility = Visibility.Visible;
                return;
            }
        }

        await RunInstallViewAsync(options);
    }

    private async Task HandleUninstallExistingAsync()
    {
        if (existingInstall is null || configureErrorText is null) return;

        string exePath = Path.Combine(existingInstall.InstallLocation, InstallEngine.ExecutableName);
        if (File.Exists(exePath))
        {
            try
            {
                var psi = new ProcessStartInfo(exePath, "--uninstall") { UseShellExecute = true };
                using var proc = Process.Start(psi);
                if (proc is not null)
                    await proc.WaitForExitAsync();
                existingInstall = InstallEngine.DetectExistingInstall();
                ShowConfigureView();
                return;
            }
            catch (Exception ex)
            {
                configureErrorText.Text = ex.Message;
                configureErrorText.Visibility = Visibility.Visible;
                return;
            }
        }

        try
        {
            if (existingInstall.AllUsers && !InstallEngine.IsElevated())
            {
                configureErrorText.Text = "Run Setup as administrator to remove the existing system-wide registration.";
                configureErrorText.Visibility = Visibility.Visible;
                return;
            }

            await InstallEngine.UninstallExistingAsync(existingInstall);
            existingInstall = InstallEngine.DetectExistingInstall();
            ShowConfigureView();
        }
        catch (Exception ex)
        {
            configureErrorText.Text = ex.Message;
            configureErrorText.Visibility = Visibility.Visible;
        }
    }

    private async Task RunInstallViewAsync(InstallOptions options)
    {
        var dock = new DockPanel();
        installCts = new CancellationTokenSource();

        var cancelButton = Button("Cancel", () =>
        {
            installCts?.Cancel();
            if (progressStatusText is not null) progressStatusText.Text = "Cancelling…";
        });
        cancelButton.MinWidth = 96;
        cancelButton.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(cancelButton, Dock.Bottom);
        dock.Children.Add(cancelButton);

        var content = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var heading = Text($"Installing {InstallEngine.AppName}…", 16, bold: true);
        heading.Margin = new Thickness(0, 0, 0, 12);

        progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 0,
            Height = 6
        };

        progressPercentText = Muted("0%", 12);
        progressStatusText = Muted("Preparing installation…", 12);
        var statusRow = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(progressPercentText, Dock.Right);
        statusRow.Children.Add(progressPercentText);
        statusRow.Children.Add(progressStatusText);

        var destHint = Muted($"Destination: {options.InstallDirectory}", 11);
        destHint.Margin = new Thickness(0, 8, 0, 0);

        content.Children.Add(heading);
        content.Children.Add(progressBar);
        content.Children.Add(statusRow);
        content.Children.Add(destHint);
        dock.Children.Add(Card(content, new Thickness(18)));

        stageHost.Content = dock;

        var progress = new Progress<InstallProgress>(update =>
        {
            int pct = (int)Math.Clamp(Math.Round(update.Fraction * 100), 0, 100);
            if (progressBar is not null) progressBar.Value = pct;
            if (progressPercentText is not null) progressPercentText.Text = $"{pct}%";
            if (progressStatusText is not null) progressStatusText.Text = update.Status;
        });

        try
        {
            await InstallEngine.InstallAsync(options, progress, installCts.Token);
            installedDir = options.InstallDirectory;
            if (options.Update)
            {
                FinishAndMaybeLaunch(forceLaunch: true);
                return;
            }
            ShowCompleteView();
        }
        catch (OperationCanceledException)
        {
            ShowErrorView("Installation was cancelled.");
        }
        catch (Exception ex)
        {
            ShowErrorView(ex.Message);
        }
    }

    private void ShowCompleteView()
    {
        var dock = new DockPanel();

        var openFolderButton = Button("Open folder", () =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(installedDir) { UseShellExecute = true });
            }
            catch
            {
                // Ignore shell open failure.
            }
        });

        var finishButton = new Button
        {
            Content = "Finish",
            MinWidth = 110,
            MinHeight = 32,
            Padding = new Thickness(14, 6, 14, 6),
            IsDefault = true
        };
        finishButton.SetResourceReference(StyleProperty, "AccentButtonStyle");
        finishButton.SetResourceReference(Control.BackgroundProperty, SystemColors.HighlightBrushKey);
        finishButton.SetResourceReference(Control.ForegroundProperty, SystemColors.HighlightTextBrushKey);
        finishButton.Click += (_, _) => FinishAndMaybeLaunch();

        var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
        DockPanel.SetDock(finishButton, Dock.Right);
        DockPanel.SetDock(openFolderButton, Dock.Left);
        footer.Children.Add(finishButton);
        footer.Children.Add(openFolderButton);
        footer.Children.Add(new Border());
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);

        var checkGlyph = new TextBlock
        {
            Text = "\uE73E",
            FontFamily = IconFont,
            FontSize = 22,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var badge = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(22),
            Background = GreenBrush,
            Child = checkGlyph,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var cardContent = new StackPanel();
        cardContent.Children.Add(badge);
        cardContent.Children.Add(Text($"{InstallEngine.AppName} is ready", 18, bold: true));
        var pathMsg = Muted($"Installed to {installedDir}", 12);
        pathMsg.Margin = new Thickness(0, 6, 0, 14);
        cardContent.Children.Add(pathMsg);

        launchCheck = new CheckBox
        {
            Content = $"Launch {InstallEngine.AppName}",
            IsChecked = true
        };
        cardContent.Children.Add(launchCheck);

        dock.Children.Add(Card(cardContent, new Thickness(20)));
        stageHost.Content = dock;
    }

    private void FinishAndMaybeLaunch(bool forceLaunch = false)
    {
        if (forceLaunch || launchCheck?.IsChecked == true)
        {
            string exePath = Path.Combine(installedDir, InstallEngine.ExecutableName);
            if (File.Exists(exePath))
            {
                try
                {
                    if (InstallEngine.IsElevated())
                    {
                        // Launch via Explorer so the desktop app starts non-elevated.
                        Process.Start(new ProcessStartInfo("explorer.exe", QuoteArg(exePath)) { UseShellExecute = false });
                    }
                    else
                    {
                        Process.Start(new ProcessStartInfo(exePath)
                        {
                            UseShellExecute = true,
                            WorkingDirectory = installedDir
                        });
                    }
                }
                catch
                {
                    // Do not block closing setup if launch fails.
                }
            }
        }

        Application.Current.Shutdown(0);
    }

    private void ShowErrorView(string message)
    {
        var dock = new DockPanel();

        var backButton = Button("Back", ShowConfigureView);
        var closeButton = Button("Close", () => Application.Current.Shutdown(1));
        closeButton.MinWidth = 96;
        var buttons = Horizontal(backButton, closeButton);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        buttons.Margin = new Thickness(0, 14, 0, 0);
        DockPanel.SetDock(buttons, Dock.Bottom);
        dock.Children.Add(buttons);

        var content = new StackPanel();
        var icon = new TextBlock
        {
            Text = "\uEA39",
            FontFamily = IconFont,
            FontSize = 26,
            Foreground = ErrorBrush,
            Margin = new Thickness(0, 0, 0, 10)
        };
        content.Children.Add(icon);
        content.Children.Add(Text("Installation could not be completed", 17, bold: true));
        var msg = Muted(message, 12);
        msg.Margin = new Thickness(0, 8, 0, 0);
        content.Children.Add(msg);

        dock.Children.Add(Card(content, new Thickness(20)));
        stageHost.Content = dock;
    }

    public async Task RunSmokeTestAsync()
    {
        string outputDir = Path.Combine(AppContext.BaseDirectory, "InstallerSmokeResults");
        try
        {
            Directory.CreateDirectory(outputDir);
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            ThemeMode = ThemeMode.Light;
            UpdateLayout();
            Capture(this, Path.Combine(outputDir, "installer-light.png"));

            ThemeMode = ThemeMode.Dark;
            UpdateLayout();
            Capture(this, Path.Combine(outputDir, "installer-dark.png"));

            File.WriteAllText(
                Path.Combine(outputDir, "passed.txt"),
                "Windows 11 Fluent installer UI rendered in Light and Dark themes.");
            Application.Current.Shutdown(0);
        }
        catch (Exception ex)
        {
            WriteSmokeFailure(ex);
            Application.Current.Shutdown(1);
        }
    }

    public static void WriteSmokeFailure(Exception ex)
    {
        string outputDir = Path.Combine(AppContext.BaseDirectory, "InstallerSmokeResults");
        Directory.CreateDirectory(outputDir);
        File.WriteAllText(Path.Combine(outputDir, "failure.txt"), ex.ToString());
    }

    private static void Capture(Window window, string filePath)
    {
        window.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(window.ActualWidth));
        int height = Math.Max(1, (int)Math.Ceiling(window.ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(filePath);
        encoder.Save(stream);
    }

    private static TextBlock Text(string text, double size = 12, bool bold = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static TextBlock Muted(string text, double size = 11)
    {
        var t = Text(text, size);
        t.Opacity = 0.68;
        return t;
    }

    private static Button Button(string text, Action action)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(12, 6, 12, 6),
            MinHeight = 32,
            Margin = new Thickness(3)
        };
        b.Click += (_, _) => action();
        AutomationProperties.SetName(b, text);
        return b;
    }

    private static StackPanel Horizontal(params UIElement[] children)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        foreach (var child in children) panel.Children.Add(child);
        return panel;
    }

    private static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Child = child,
        Padding = padding ?? new Thickness(14),
        Background = SubtleBrush,
        CornerRadius = new CornerRadius(8)
    };

    private static string FormatBytes(long bytes)
    {
        const double mb = 1024.0 * 1024.0;
        const double gb = 1024.0 * mb;
        if (bytes >= gb) return $"{bytes / gb:0.0} GB";
        return $"{Math.Max(1, Math.Round(bytes / mb)):0} MB";
    }

    private static string QuoteArg(string value) =>
        $"\"{value.TrimEnd('\\').Replace("\"", "\\\"")}\"";
}
