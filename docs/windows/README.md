# Joonga Tag for Windows

A native Windows metadata editor and transfer utility, built with C#, .NET 10 WPF, Microsoft's Fluent controls and the Windows 11 Mica backdrop. It follows the system's light, dark, accent and high-contrast settings. The title bar puts app commands alongside the window controls. Drag the space between command groups to move the window, or double-click it to maximise or restore.

## Install or Run

**Installer (recommended):**
1. Choose **Joonga-Tag-*-Windows-x64-Setup.exe** for Intel/AMD PCs, or **Joonga-Tag-*-Windows-arm64-Setup.exe** for Windows on ARM.
2. Run the Windows 11 Setup wizard to install to `Program Files\Joonga Tag` (all users), `%LocalAppData%\Programs\Joonga Tag` (current user), or a custom folder you choose, with optional Start Menu and Desktop shortcuts.
3. Uninstall anytime from Windows **Settings → Apps → Installed apps** (or by running `"Joonga Tag.exe" --uninstall`).

**Portable ZIP:**
1. Choose the **win-x64** ZIP for Intel/AMD PCs, or **win-arm64** for Windows on ARM.
2. Extract the entire folder. Run **Joonga Tag.exe** inside it.
3. Keep `tools`, `exiftool_files`, runtime DLLs and the other supporting files with the executable. This is a portable folder, not a single-file executable.

The .NET runtime and ExifTool are included. Administrator access and a separate .NET installation are not required. The optional map preview uses the Microsoft Edge WebView2 Runtime normally present on Windows 11. If it is unavailable, the map window reports that requirement. Metadata features do not depend on WebView2 or an internet connection.

Windows 11 22H2 or later is the target. Windows can substitute a solid background when Mica/transparency is disabled or unavailable. The app is not Authenticode-signed. The ARM64 app uses native ARM64 .NET/WPF and the official x64 ExifTool executable through Windows 11's x64 emulation.

## Compare and copy

The title bar switches between Metadata, Map and Transfer. Metadata and Map share the file list and selection. Drag the divider to resize the file pane and press Ctrl+I to toggle the Metadata inspector. Right-click a column header to choose columns or reset to defaults. Drag headers to reorder them. Each workspace remembers its column layout. Use the toolbar trash icon to clear the current file list, or right-click a row and choose **Remove from list**. Use **More actions** for navigation, Quick preview, location commands and operation results.

Choose folders or drop folders/individual files into either pane. A single folder replaces that pane; loose files or several folders append. Nested folders are scanned. Reparse-point subdirectories are skipped, and inaccessible folders report an error rather than silently pretending every file was scanned. Clear a pane from its folder menu.

Names match without extensions, case-insensitively. A trailing `-HDR` may match the unsuffixed original. Photos and videos are matched separately. Ambiguous originals and same-path files are skipped. Green status marks identify matched files. Alternating stripes extend through empty list space. Click a pane’s folder icon to open its location in Explorer, or to choose a folder when one has not been set. The pane menu can copy the folder path. Headers show folder paths, with full paths on hover; row tooltips contain full file paths.

**Copy metadata** processes all matched exports after showing the count. ExifTool copies writable tags, keeps `_original` backups and preserves the file modification time. No video or image encoding is performed. Some formats cannot store every tag. Per-file warnings and failures remain visible in the results window.

## Select, preview and edit

- Ctrl-click or Shift-click to select multiple files; Ctrl+A selects the active pane.
- The inspector shows the selected files' values, grouped by category. Search by tag, group or value. Use **Differences only** for comparison.
- Enable **Edit metadata** to see bordered input fields. Missing fields remain editable. Common camera, lens, capture, date, descriptive and GPS fields are offered even on stripped exports.
- One edited field stages the same value for every selected file. Different/missing values appear as orange **Multiple values**. Values from matched originals contribute available tag names, never pretend to be values already present in the selected exports.
- **Save changes** writes staged edits; **Revert** clears drafts for the selection. Empty values remove a tag. Successful files clear their drafts; failed/warning files retain them for review.
- Make and Model merge across EXIF/XMP/QuickTime aliases; writes synchronize existing aliases and are read back for verification. Lens ID, Aperture and Shutter Speed route to Lens Model, F Number and Exposure Time. Other computed fields are labelled read-only.
- Add any other writable ExifTool tag with **Add another tag**.
- Space or double-click opens Quick preview; Space/Escape closes it. Arrow keys navigate a multi-file selection. Video playback uses Windows codecs and provides Open externally when unsupported.
- Right-click for preview, Open, Show in Explorer, Copy paths or Apply workflow.
- **Map** plots saved and staged GPS and groups nearby photo pins with a count badge. Open a group to zoom in, select one file, or select every member. Select files before clicking a point or choosing a search result to stage coordinates. Use the toolbar **Favourites** button and the file context menu’s **Copy location** and **Paste location** commands (Ctrl+Shift+C/V) to reuse locations. Use **Map options** to fit or filter pins and switch between Normal (OpenStreetMap), Hybrid and Satellite (Esri). Tiles and Photon search require internet access and the map requires Edge WebView2. **Save changes** writes staged edits across modes.

Shell thumbnails use Windows' installed codecs. AVIF/HEIF/RAW and less common video formats may need Windows codec extensions to display thumbnails or play in the preview; this does not prevent ExifTool from reading/writing supported metadata.

## Workflows and settings

Select files in either pane, click the **Workflows** icon in the toolbar, and choose a saved workflow to stage it for that selection. The list refreshes whenever you open the menu. Use **Manage Workflows** to edit presets or choose a different target scope.

Joonga Tag copies settings and workflows from the old `Metadata Transfer` folders when the corresponding new file is missing. After verifying the data in Joonga Tag, you can remove the legacy files. Workflows use the same versioned JSON archive as the Mac application. Import/export individual workflows, multiple selected workflows, entire libraries or several JSON files together. Import validates every input before saving and preserves operation order. Personal presets are kept in your profile.

**New**, **Duplicate**, **Delete** and **Save workflow** manage presets. The editor is one large text box: one `-Tag=Value` or `-Destination<Source` operation per line, with no shell quoting. Standard Ctrl+C/V/X/A/Z/Y shortcuts work. Invalid lines are identified before applying. Unsaved workflow changes prompt Save/Discard/Cancel when leaving the editor. Targets default to the active file selection; all-originals/all-exports scopes are also available.

- Workflows: `%APPDATA%\Joonga Tag\Workflows.json`
- Preferences: `%LOCALAPPDATA%\Joonga Tag\Settings.json`
- Map browser cache: `%LOCALAPPDATA%\Joonga Tag\WebView2`

Open the **Settings** gear or press Ctrl+comma to choose or clear the default exports folder, show or hide the preview, open the workflow library, and configure ExifTool. The default exports folder is used on the next launch. You can also save the current export folder from its pane menu. If you configure another ExifTool installation, rename `exiftool(-k).exe` to `exiftool.exe`, and keep its full `exiftool_files` folder alongside it. Closing the main window exits the app. Pending writes must finish (or be cancelled between files); unsaved edits prompt before exit.

## Shortcuts

| Shortcut | Action |
| --- | --- |
| Ctrl+N | New workflow |
| Ctrl+S | Save metadata / save workflow when its window is active |
| Ctrl+I | Show/hide inspector |
| F5 | Refresh comparison |
| Ctrl+Alt+[ or Ctrl+Alt+] | Previous or next file in the active pane |
| Space | Open/close quick preview from file lists |
| Ctrl/Shift-click, Ctrl+A | Extended file selection |
| Delete in workflow list | Delete selected workflows |

## Build and test

Install the **.NET 10 SDK**, open PowerShell at the repository root, then run:

```powershell
.\windows\Build.ps1 -Runtime win-x64
# Or native Windows on ARM application:
.\windows\Build.ps1 -Runtime win-arm64
```

The script downloads the pinned official ExifTool distribution, verifies its SHA-256, publishes a self-contained app, runs tests, and creates a ZIP under `windows/build/`. Supply `-ExifToolFolder` to use an already-extracted complete distribution. GitHub Actions builds both architectures and runs the x64 window smoke test.

To run just the portable engine tests:

```powershell
dotnet run --project windows/MetadataTransfer.Tests -- C:\path\to\exiftool.exe
```

For a native UI startup/layout smoke test, run the built app with `--smoke-test`. It uses isolated settings and synthetic files and writes PNGs/logs under `SmokeResults` next to the executable. It never loads your workflow library or modifies your media.

## Validation status

The source has been cross-compiled on macOS for Windows. Portable engine checks cover matching, workflow compatibility/persistence, mixed and missing batch fields, calculated-tag routing, actual JPEG/MP4 writes/copies, camera read-back verification, backup retention, literal process arguments and modification-time preservation.

**This build has not been run on Windows in the current development session.** Mica rendering, Shell thumbnails, Windows video decoding, map runtime integration, Windows file dialogs, DPI scaling and keyboard/mouse interaction require the supplied Windows smoke test and manual checklist in `WINDOWS-QA.md`. A clean cross-build is not a claim of completed Windows visual QA.

## Source layout

In [windows](../../windows), `MetadataTransfer.Core` contains platform-independent matching, metadata, ExifTool and workflow storage. `MetadataTransfer.Windows` contains the native UI, uninstaller and OS integration. `MetadataTransfer.Installer` builds the self-contained Windows 11 Setup executable. `MetadataTransfer.Tests` contains a dependency-free regression runner and synthetic fixtures. Build output and personal libraries are excluded from the source ZIP and `.gitignore`.
