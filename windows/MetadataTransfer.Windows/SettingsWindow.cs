using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MetadataTransfer.Core;

namespace MetadataTransfer.Windows;

internal sealed class SettingsWindow : Window
{
    public SettingsWindow(
        Window owner, Settings settings, Func<string?> exifToolPath,
        Action<Window> chooseExifTool, Action resetExifTool,
        Action save, Action<bool> togglePreview, Action openWorkflowLibrary)
    {
        Owner = owner;
        Title = "Settings";
        ThemeMode = ThemeMode.System;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Width = 610;
        Height = 520;
        MinWidth = 480;
        MinHeight = 360;
        ShowInTaskbar = false;

        var layout = new DockPanel { Margin = new Thickness(24) };
        var title = UI.Text("Settings", 23, true);
        title.Margin = new Thickness(0, 0, 0, 16);
        DockPanel.SetDock(title, Dock.Top);
        layout.Children.Add(title);
        var done = UI.Button("Done", Close);
        done.IsCancel = true;
        done.HorizontalAlignment = HorizontalAlignment.Right;
        DockPanel.SetDock(done, Dock.Bottom);
        layout.Children.Add(done);

        var sections = new StackPanel();
        void AddSection(string title, params UIElement[] controls)
        {
            var section = new StackPanel();
            section.Children.Add(UI.Text(title, 14, true));
            foreach (var control in controls) section.Children.Add(control);
            var card = UI.Card(section);
            card.Margin = new Thickness(0, 0, 0, 10);
            sections.Children.Add(card);
        }

        var folderPath = UI.Muted("");
        var openFolder = UI.Button("Open", () =>
        {
            if (settings.DefaultExports is string folder && Directory.Exists(folder)) UI.Open(folder);
        });
        var clearFolder = UI.Button("Clear", () => { });
        void UpdateFolder()
        {
            folderPath.Text = settings.DefaultExports ?? "No default folder";
            folderPath.ToolTip = settings.DefaultExports;
            openFolder.IsEnabled = Directory.Exists(settings.DefaultExports);
            clearFolder.IsEnabled = settings.DefaultExports is not null;
        }
        clearFolder.Click += (_, _) =>
        {
            settings.DefaultExports = null;
            save();
            UpdateFolder();
        };

        var chooseFolder = UI.Button("Choose…", () =>
        {
            var picker = new OpenFolderDialog { Title = "Set default exports folder" };
            if (Directory.Exists(settings.DefaultExports)) picker.InitialDirectory = settings.DefaultExports!;
            if (picker.ShowDialog(this) != true) return;
            settings.DefaultExports = picker.FolderName;
            save();
            UpdateFolder();
        });
        var showPreview = new CheckBox
        {
            Content = "Show preview pane",
            IsChecked = settings.ShowInspector,
            Margin = new Thickness(0, 8, 0, 0)
        };
        showPreview.Checked += (_, _) => togglePreview(true);
        showPreview.Unchecked += (_, _) => togglePreview(false);
        UpdateFolder();
        AddSection("Workspace", UI.Muted("Default exports folder"), folderPath,
            UI.Horizontal(chooseFolder, openFolder, clearFolder), showPreview);

        var exifPath = UI.Muted("");
        void UpdateExifTool()
        {
            exifPath.Text = exifToolPath() ?? "ExifTool is unavailable";
            exifPath.ToolTip = exifToolPath();
        }
        var chooseTool = UI.Button("Choose…", () => { chooseExifTool(this); UpdateExifTool(); });
        var automaticTool = UI.Button("Use automatic detection", () => { resetExifTool(); UpdateExifTool(); });
        UpdateExifTool();
        AddSection("Tools and data", UI.Muted("ExifTool executable"), exifPath,
            UI.Horizontal(chooseTool, automaticTool),
            UI.Horizontal(UI.Button("Open Workflow Library Folder", openWorkflowLibrary)));

        layout.Children.Add(new ScrollViewer { Content = sections, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = layout;
    }
}
