# Joonga Metadata

<img src="assets/windows-icon.png" alt="Joonga Metadata icon" width="96">

Compare and edit photo and video metadata on macOS and Windows. Match originals with exports, copy writable tags, and apply saved workflows to a selection of files. Both apps use ExifTool and share a portable workflow format.

The app opens in **Preview**, with sortable file, tag count, camera, date and GPS columns. **Metadata** opens a left file list and a grouped editor. **Map** plots loaded media with saved or pending GPS, and **Transfer** keeps the original-to-export matching workspace. Preview, Metadata and Map share the same file list and selection.

Manual edits, workflow assignments and map pins stay pending across files and modes. **Save Changes** writes the full batch with ExifTool backups. On the map, select one or more files to stage coordinates by clicking or choosing a search result. Choose Normal, Hybrid or Satellite, filter to selected files, and use Favourites or Copy/Paste Location to reuse coordinates.

## Downloads

[GitHub Releases](https://github.com/jaisinghdhaliwal/joonga-metadata/releases) contains downloads for macOS (Apple silicon) and Windows (x64 and ARM64). See the platform guides below for requirements and setup.

## How it works

1. Drop original and exported files or folders into their respective panes. The app matches filenames across extensions and recognises HDR suffixes. Click a pane’s folder icon to open its location in Finder or Explorer.
2. Select files to inspect metadata differences or enter new values in the preview pane.
3. Copy metadata to matched exports or apply a saved workflow. ExifTool keeps backups beside modified files.

## Build from source

| Platform | Build from the repository root | Guide |
| --- | --- | --- |
| macOS | `./macos/build.command` | [macOS setup and usage](docs/macos.md) |
| Windows x64 | `.\windows\Build.ps1 -Runtime win-x64` | [Windows setup and usage](docs/windows/README.md) |
| Windows ARM64 | `.\windows\Build.ps1 -Runtime win-arm64` | [Windows setup and usage](docs/windows/README.md) |

## Repository layout

- `macos/` contains the SwiftUI and AppKit app and its build script.
- `windows/` contains the WPF app, metadata engine, and regression tests in one [Visual Studio solution](windows/JoongaMetadata.slnx).
- `assets/` contains the [Windows icon SVG](assets/joonga-metadata-windows-icon.svg), the [Windows icon preview](assets/windows-icon.png), and the [macOS logo SVG](assets/joonga-metadata-mac-logo.svg).
- `docs/` contains platform guides, testing notes, and [third-party notices](docs/windows/THIRD-PARTY-NOTICES.md).
- `.github/workflows/` contains the macOS build and the Windows build and smoke test.
