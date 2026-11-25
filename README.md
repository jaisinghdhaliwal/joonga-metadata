<p align="center">
  <img src="assets/windows-icon.png" alt="Joonga Metadata stacked tags" width="112">
</p>

<h1 align="center">Joonga Metadata</h1>

<p align="center">
  Compare, edit, and transfer photo and video metadata on macOS and Windows.
</p>

## Downloads

Get the latest builds from [GitHub Releases](https://github.com/jaisinghdhaliwal/joonga-metadata/releases).

| macOS | Windows |
| :---: | :---: |
| <img src="assets/macos-icon.png" alt="Joonga Metadata macOS app icon" width="64"> | <img src="assets/windows-icon.png" alt="Joonga Metadata Windows app icon" width="64"> |
| Apple silicon release · Universal build from source | x64 and ARM64 releases |
| [macOS setup and usage](docs/macos.md) | [Windows setup and usage](docs/windows/README.md) |

## Features

- **Metadata** sorts photos and videos, previews the selection in a collapsible inspector, groups tags for editing, and applies reusable workflows. Both apps use ExifTool and share a portable workflow format.
- **Map** plots saved and pending GPS coordinates. Search for a place, pick a point, or reuse a favourite or copied location. Switch between Normal, Hybrid, and Satellite views or show only selected files.
- **Transfer** matches originals to exports and copies writable metadata, including across file extensions and HDR filename variants.

Metadata and Map share a resizable file pane and selection, with separate column layouts. Manual edits, workflow assignments, and map changes stay pending across files and modes until you choose **Save Changes**. ExifTool keeps backups when writing files.

## How it works

1. Drop original and exported files or folders into their respective panes. The app matches filenames across extensions and recognises HDR suffixes. Click a pane’s folder icon to open its location in Finder or Explorer.
2. Select files to inspect metadata differences or enter new values in the preview pane.
3. Copy metadata to matched exports or apply a saved workflow. ExifTool keeps backups beside modified files.

## Build from source

| Platform | Build from the repository root | Guide |
| --- | --- | --- |
| macOS · Apple silicon and Intel | `bash macos/build.command` | [macOS setup and usage](docs/macos.md) |
| Windows x64 | `.\windows\Build.ps1 -Runtime win-x64` | [Windows setup and usage](docs/windows/README.md) |
| Windows ARM64 | `.\windows\Build.ps1 -Runtime win-arm64` | [Windows setup and usage](docs/windows/README.md) |

## Repository layout

- `macos/` contains the SwiftUI and AppKit app and its build script.
- `windows/` contains the WPF app, metadata engine, and regression tests in one [Visual Studio solution](windows/JoongaMetadata.slnx).
- `assets/` contains the [macOS](assets/macos-icon.png) and [Windows](assets/windows-icon.png) icon previews, plus the [macOS logo SVG](assets/joonga-metadata-mac-logo.svg) and [Windows icon SVG](assets/joonga-metadata-windows-icon.svg).
- `docs/` contains platform guides, testing notes, and [third-party notices](docs/windows/THIRD-PARTY-NOTICES.md).
- `.github/workflows/` contains the macOS build and the Windows build and smoke test.
