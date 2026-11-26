# Joonga Tag for macOS

A native macOS application for comparing, editing and copying media metadata. Built with SwiftUI, AppKit, Quick Look and MapKit. The universal build runs on Apple silicon and Intel Macs with macOS 13 or later.

ExifTool 13.64 is bundled inside the app (`Contents/Resources/exiftool/`), so metadata operations work without a separate installation. If you install a newer ExifTool separately (for example via Homebrew at `/opt/homebrew/bin/exiftool`), you can point Joonga Tag to it in **Settings** and restore the app-bundled ExifTool at any time.

## Build

Install Xcode 26 or newer and run `bash macos/build.command` from the repository root to compile the layered Icon Composer icon, download and verify the pinned ExifTool distribution (`13.64`), and assemble the app bundle. Set `EXIFTOOL_DIR` to an already-extracted ExifTool directory to build without downloading. If you already compiled this icon into an asset catalog, run `bash macos/build.command --icon-catalog /path/to/Assets.car` to reuse it. With Command Line Tools only, run `bash macos/build.command --compat-icon` to use the static compatibility icon. Both architectures are built and combined into `macos/build/Joonga Tag.app`.

## Workflow library

Select files in either pane and click a saved workflow in the **Workflows** menu to stage it for that selection. The list refreshes whenever you open the menu. Use **Manage Workflows** to edit presets or choose a different target scope.

Joonga Tag stores workflows at `~/Library/Application Support/Joonga Tag/Workflows.json` and settings in `~/Library/Preferences/com.jaisinghdhaliwal.joongatag.plist`. It copies existing data from `~/Library/Application Support/Metadata Transfer/Workflows.json` and the preference domain `local.codex.metadatapreview` when migrating. After confirming your workflows and settings in Joonga Tag, you can remove those legacy files. New installations start with an empty workflow library.

Open Workflows from the toolbar. Use New Workflow or Command-N, name it, and enter ExifTool metadata operations in the large editor, one per line. Save Workflow or Command-S creates or updates that workflow. Duplicate starts a separate copy. Select one or multiple workflows and use Delete, or press Delete while the workflow list has focus. Import accepts several JSON libraries. Export saves selected workflows or the complete library as a portable, versioned JSON file. Import merges exact duplicates and validates all selected files before saving.

Workflow operations run directly through ExifTool. They are metadata assignments or tag-copy operations, without a shell. The editor validates each line before applying. Unsaved changes prompt you to save, discard or cancel when switching workflows or closing the editor. Targets default to the files selected in the active pane. ExifTool keeps backups. A scrollable, selectable results window shows per-file output and offers Copy Results.

## File comparison and editing

Drop files or folders into either pane or choose folders from the toolbar. Folder scans include subfolders. Names match case-insensitively without extensions, and a trailing -HDR can match an original without that suffix. Ambiguous names are skipped. The same file cannot be a copy target for itself. Command-click selects separate files, Shift-click selects ranges, and Command-A selects the active list.

Search metadata by tag, group or value in the preview. Preview compares tags across the selection. Different or missing values show Multiple values in orange. Enable Edit metadata and type into the outlined fields, including empty fields marked Enter value. Unsaved changes are marked until saved. Lens ID, Aperture and Shutter Speed edit the underlying Lens Model, F Number and Exposure Time tags. Other calculated values are labelled Calculated; file properties are labelled Read-only. Common camera, lens, exposure, date, descriptive and GPS fields remain available in edit mode even when absent. Batch inspection includes tag names from the matched originals, but values always come from the selected targets. Differences filtering does not hide fields while editing. Save applies edits to all selected files. Make and Model are consolidated across metadata groups. Read-only and unsupported fields may be displayed; ExifTool reports writes it cannot perform. Orientation and colour metadata can affect display. Location tags include an Apple Maps preview when GPS coordinates are available. Map tiles require network access.

Right-click for Quick Look, Open, Reveal in Finder, Copy Path and Apply Workflow. Space opens and closes native Quick Look. A visible file thumbnail provides the zoom origin. Offscreen thumbnails and Reduce Motion use the regular fade. Click a pane’s folder icon to open its folder in Finder. When a pane has no folder, its icon opens the folder picker. File paths appear in pane headers with full paths available on hover. Use the pane menu to copy the folder path. Closing the last window quits the app.

### Map workspace

Add files or folders in Preview, Metadata or Map. Select files in Map and click a point or choose a place search result to stage GPS. Copy Location takes the saved or pending coordinates of one selected file. Paste Location applies those coordinates to the current selection. Use Map Options to filter to selected files or choose Normal, Hybrid or Satellite. Save reusable places under Favourites. Click **Save Changes** to write every pending file. Apple Maps tiles and place search require a connection.

## Settings and menus

Set **System Settings → Appearance → Accent colour** to **Multicolour** to use Joonga coral for in-app controls and active file selection. Any explicit system accent, including Blue or Graphite, takes precedence. File selection uses a quieter highlight when the workspace is inactive.

Open **Joonga Tag → Settings** with Command-comma. Set the default exports folder for future launches, choose whether to show the preview on startup, change the ExifTool path or restore the app-bundled ExifTool, or open the workflow library folder. Use **File** for choosing and opening folders, copying metadata, and saving edits. **View** contains preview, navigation, and refresh commands. **Workflows** opens the editor and library.

## Shortcuts

The native menus provide Cut, Copy, Paste, Select All, Undo and Redo, plus Command-N for New Workflow, Command-S for Save Workflow and Command-W for Close Window. Use Option-Command-[ and Option-Command-] to move between files in the active pane. Text editors retain ordinary text entry while file lists support native selection shortcuts.

## Source

In [macos](../macos), `Sources/` contains the app interface, workflow management, metadata reads/writes and file actions. `Info.plist` defines the app bundle. `AppIcon.icon` is the editable macOS icon source. `AppIcon.icns` is the static fallback derived from that design. `.gitignore` excludes build output and common local files. Exported personal libraries should be kept outside a public repository.

## Icon Composer artwork

Import the [stacked tags SVG](../assets/joonga-tag-mac-logo.svg) as a single layer, or import the [separate SVGs](../assets/icon-composer) to style each tag independently. Put the coral tag above the paper tag and set the background to navy (`#131E29`) in Icon Composer. Both layers use the same 1024 × 1024 canvas and retain their relative placement. [Apple’s import guide](https://developer.apple.com/documentation/xcode/creating-your-app-icon-using-icon-composer)

## Validation

Tested workflow creation, updating an existing workflow, deletion, import deduplication and reload from disk. UI checks exercised Command-N, Command-A, Copy, Paste, Undo, Redo, Command-S and workflow deletion. Metadata regression checks covered multi-file edits, camera aliases, GPS references, folder drops and workflow archive roundtrips. Native context menus and Space-bar Quick Look were exercised with media files.
