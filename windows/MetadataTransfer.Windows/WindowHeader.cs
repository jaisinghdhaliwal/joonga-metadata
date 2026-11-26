using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Shell;
using System.Windows.Threading;

namespace MetadataTransfer.Windows;

internal sealed class WindowHeader : Border
{
    private const double HeaderHeight = 42;
    private const int CaptionButtonBounds = 5;
    private const double DefaultCaptionInset = 152;
    private readonly Window window;
    private readonly DockPanel layout;
    private readonly StackPanel identity;
    private readonly WindowChrome chrome;

    public WindowHeader(Window window, UIElement folderCommands, UIElement viewCommands, UIElement modeCommands)
    {
        this.window = window;
        Height = HeaderHeight;
        Background = System.Windows.Media.Brushes.Transparent;
        BorderBrush = UI.Subtle;
        BorderThickness = new Thickness(0, 0, 0, 1);

        layout = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, DefaultCaptionInset, 0) };
        Child = layout;

        var appIcon = new Image
        {
            Source = window.Icon, Width = 16, Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 8, 0)
        };
        var title = UI.Text(window.Title, 12);
        title.TextWrapping = TextWrapping.NoWrap;
        identity = UI.Horizontal(appIcon, title);
        identity.Margin = new Thickness(0, 0, 14, 0);
        DockPanel.SetDock(identity, Dock.Left);
        layout.Children.Add(identity);

        DockPanel.SetDock(folderCommands, Dock.Left);
        WindowChrome.SetIsHitTestVisibleInChrome(folderCommands, true);
        layout.Children.Add(folderCommands);
        DockPanel.SetDock(modeCommands, Dock.Left);
        WindowChrome.SetIsHitTestVisibleInChrome(modeCommands, true);
        layout.Children.Add(modeCommands);

        DockPanel.SetDock(viewCommands, Dock.Right);
        WindowChrome.SetIsHitTestVisibleInChrome(viewCommands, true);
        layout.Children.Add(viewCommands);
        layout.Children.Add(new Border()); // Native caption drag and double-click area.

        var resize = SystemParameters.WindowResizeBorderThickness;
        chrome = new WindowChrome
        {
            CaptionHeight = Math.Max(0, HeaderHeight - resize.Top),
            ResizeBorderThickness = resize,
            NonClientFrameEdges = NonClientFrameEdges.Left | NonClientFrameEdges.Right | NonClientFrameEdges.Bottom,
            // The full glass frame keeps Fluent WPF's DWM Mica backdrop visible.
            // DWM also draws the caption buttons and owns their hit-testing.
            GlassFrameThickness = WindowChrome.GlassFrameCompleteThickness,
            UseAeroCaptionButtons = true
        };
        ApplyChrome();

        Loaded += (_, _) => QueueCaptionMeasurement();
        SizeChanged += (_, _) => UpdateCaptionInset();
        window.StateChanged += (_, _) => UpdateHeaderGeometry();
        window.DpiChanged += (_, _) => QueueCaptionMeasurement();
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        window.Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
    }

    private void OnSystemParametersChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
            Dispatcher.InvokeAsync(ApplyChrome);
    }

    private void ApplyChrome()
    {
        bool integrated = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000) && !SystemParameters.HighContrast;
        // Use the standard title bar when accessibility settings disable the glass frame.
        WindowChrome.SetWindowChrome(window, integrated ? chrome : null);
        identity.Visibility = integrated ? Visibility.Visible : Visibility.Collapsed;
        layout.Margin = integrated ? new Thickness(0, 0, DefaultCaptionInset, 0) : new Thickness(12, 0, 12, 0);
        UpdateHeaderGeometry();
    }

    private void UpdateHeaderGeometry()
    {
        bool integrated = WindowChrome.GetWindowChrome(window) == chrome;
        // The top resize border extends outside the work area when maximized.
        double topInset = integrated && window.WindowState == WindowState.Maximized
            ? chrome.ResizeBorderThickness.Top
            : 0;
        Padding = new Thickness(0, topInset, 0, 0);
        Height = HeaderHeight + topInset;
        chrome.CaptionHeight = Math.Max(0, Height - chrome.ResizeBorderThickness.Top);
        if (integrated) QueueCaptionMeasurement();
    }

    private void QueueCaptionMeasurement() =>
        Dispatcher.InvokeAsync(UpdateCaptionInset, DispatcherPriority.Loaded);

    private void UpdateCaptionInset()
    {
        if (!IsLoaded || WindowChrome.GetWindowChrome(window) != chrome || window.WindowState == WindowState.Minimized) return;
        var handle = new WindowInteropHelper(window).Handle;
        if (DwmGetWindowAttribute(handle, CaptionButtonBounds, out var buttons, Marshal.SizeOf<NativeRect>()) != 0 ||
            buttons.Right <= buttons.Left || !GetWindowRect(handle, out var frame)) return;

        // DWM reports physical pixels relative to the outer window. Convert the
        // left edge into the header's coordinate space to reserve room at any DPI.
        var buttonStart = PointFromScreen(new Point(frame.Left + buttons.Left, frame.Top));
        double inset = Math.Max(0, ActualWidth - buttonStart.X) + 8;
        if (inset > 0 && inset < ActualWidth && Math.Abs(inset - layout.Margin.Right) >= 1)
            layout.Margin = new Thickness(0, 0, inset, 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out NativeRect bounds, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);
}
