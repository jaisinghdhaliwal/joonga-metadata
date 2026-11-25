# Joonga Metadata — contributor and agent guide

This is the maintained handoff document for people and AI agents working on
Joonga Metadata. Read it before changing code, documentation, builds, release
assets or product behaviour.

## Mandatory context maintenance

At the end of **every task that changes code, documentation, build/release
configuration, product behaviour, validation status or a durable decision**,
update this file in the same change set. Do not defer the update.

Keep the record factual, dated and concise:

- update **Current state** after implementation, validation or release work;
- update **Architecture** when a component boundary, storage location,
  dependency, build path or data flow changes;
- update **Product rules** when a durable UX/product decision is made;
- update **Distribution** when versions, signing, installers, CI or release
  assets change;
- state uncertainty and untested work plainly—never turn an assumed result into
  a claim of verification.

Never put personal workflow libraries, settings, media, credentials, signing
material or local machine paths in this repository.

## Product

Joonga Metadata is a native desktop app for photographers and videographers to
inspect, edit, geotag and transfer photo/video metadata without re-encoding
media. It uses ExifTool for reads and writes and supports common formats,
including DNG, AVIF, JPEG, ARW and MP4.

The intended experience is closer to Apple's Finder/Photos, MetaImage and
MetaVideo than to a command-line front-end: compact, calm, thumbnail-led and
system-native while still supporting detailed batch work.

## Branding and visual provenance

Joonga Metadata is part of the personal **Joonga** brand. Its visual language
comes from Jai Singh Dhaliwal's portfolio site,
[jaisinghdhaliwal.github.io](https://jaisinghdhaliwal.github.io), rather than
from a generic developer-tool palette.

- **Joonga coral** is the brand coral established on the portfolio site. Use it
  as a restrained accent, not as a general text colour or a substitute for
  hierarchy. The coral/system-accent rule in the design section is intentional.
- The identity is **stacked metadata tags**, not a “J” monogram. Keep that
  recognisable silhouette in macOS and Windows artwork.
- The Windows icon must have no navy square background. Its red tag must be
  visually opaque at the punched hole: do not allow the white/paper tag to show
  through it.
- The macOS icon is authored in Apple Icon Composer. Preserve the current
  editable `macos/AppIcon *.icon` source and use its compiled artwork for macOS
  builds; do not silently fall back to a different generic icon.
- The product was renamed from Metadata Transfer to Joonga Metadata, but legacy
  user-data locations intentionally retain the old name for compatibility.

The design bar is “convincingly native”, not merely decorated: use Finder/Photos
and Windows 11 Settings as references for density, typography, window chrome,
focus/selection states, animation and visual restraint. Iterate visually after
changes; do not stop at a technically functional layout.

## Product rules

- Preserve media encoding. Metadata work uses ExifTool, not a transcoder.
- Stage inspector edits, workflows and map changes in memory. Do not write
  media until **Save Changes** is chosen.
- ExifTool keeps backups. Surface per-file success, warnings and failure;
  retain failed pending drafts for review.
- Multi-selection is first-class: Command/Ctrl-click selects non-contiguous
  rows, Shift-click selects ranges and Command/Ctrl-A selects the active list.
  A unified editable field applies its new value to every selected file.
- Different/missing selected values appear once as an orange multiple-value
  state. Do not duplicate Make/Model fields per selected file.
- Clearly distinguish editable, calculated and read-only metadata. Absent
  writable tags must remain editable.
- Workflows are portable, versioned JSON user data. Their editor remains one
  large ExifTool-operation text field (one operation per line); do not ship
  personal presets in source or the app.
- Full source/destination paths must be discoverable in headers/tooltips and
  pane folder icons must open/select their locations.
- Quick Look/quick preview remains available from Space and the context menu.

### Workspaces

All modes share loaded files and selection where the platform permits.

| Mode | Purpose |
| --- | --- |
| Preview | Browse, sort and inspect files and metadata. |
| Metadata | Edit categorized metadata for one or many selected files. |
| Map | Plot saved/pending GPS data, search/click a place, stage coordinates, copy/paste locations and manage favourites. |
| Transfer | Match originals to exports and copy writable metadata to matching export files. |

Matching is case-insensitive by filename stem across extensions. A trailing
`-HDR` suffix may match an unsuffixed original. Skip ambiguous and same-path
matches. Folder scans are recursive; individual files and folders can be
dropped into either pane.

### Design direction

**macOS:** use SwiftUI/AppKit system conventions. The reference is modern
Finder/Photos: restrained translucency, compact pane headers, subtle selection,
checkerboard list rows extending through empty space, native toolbar/menu
behaviour and thumbnail-originated quick look. Joonga coral is the fallback
brand accent only when System Settings → Accent Colour is Multicolour; an
explicit user accent takes precedence. Do not use coral for ordinary body text.

**Windows:** use Windows 11 conventions: Mica/transparent backdrop when
available, Fluent controls, modern icon-led commands, integrated title bar and
dense but breathable file rows. Fall back gracefully where Mica, transparency,
codecs or WebView2 are unavailable; do not replace modern UI with legacy-looking
controls unnecessarily. Map pins use media thumbnails with a white frame,
inspired by Apple Photos.

## Architecture

| Path | Responsibility |
| --- | --- |
| `macos/` | Native SwiftUI/AppKit app, bundle metadata, icon sources and build script. |
| `macos/Sources/` | macOS UI, metadata reading/writing, workflows, file actions, map/location and Quick Look. |
| `windows/MetadataTransfer.Core/` | Platform-neutral models, matching, ExifTool integration and workflow persistence. |
| `windows/MetadataTransfer.Windows/` | .NET 10 WPF UI, Mica/Fluent setup, panes, inspector, map, preview, settings and workflows. |
| `windows/MetadataTransfer.Tests/` | Dependency-free regression runner and synthetic fixtures. |
| `windows/Build.ps1` | Windows restore, pinned ExifTool verification, self-contained publish, test and ZIP build. |
| `assets/` | Icon previews, SVGs and Icon Composer artwork. |
| `docs/` | Platform setup, QA/validation and third-party notices. |
| `.github/workflows/` | macOS and Windows build/test CI. |

### macOS

- Target: macOS 13 or later; SwiftUI, AppKit, Quick Look and MapKit.
- Entry point: `macos/Sources/main.swift`.
- Build with `bash macos/build.command`. It produces an ad-hoc-signed
  universal `arm64` + `x86_64` app at
  `macos/build/Joonga Metadata.app`.
- The current editable Icon Composer source is the newest `macos/AppIcon *.icon`
  directory in the worktree. `AppIcon.icns` is only a static compatibility
  fallback; do not delete, rename or revert an Icon Composer source without
  checking the current worktree and its associated artwork.
- ExifTool discovery defaults to `/opt/homebrew/bin/exiftool` and
  `/usr/local/bin/exiftool`.
- Existing users retain legacy storage locations:
  `~/Library/Application Support/Metadata Transfer/Workflows.json`; settings
  use the app's Preferences storage.
- Apple Maps tiles and place search require a network connection.

### Windows

- Target: Windows 11 22H2 or later; C#/.NET 10 WPF.
- `MetadataTransfer.Core` must remain UI-agnostic.
- UI coordination: `App.xaml(.cs)`, `MainWindow.cs` and
  `WorkspaceModes.cs`. Key features: `FilePane.cs`, `Inspector.cs`,
  `MapPane.cs`, `WindowHeader.cs`, `SettingsWindow.cs`,
  `WorkflowWindow.cs` and `WorkspaceStyles.xaml`.
- Microsoft WebView2 is required only for the in-app map. Normal maps use
  OpenStreetMap; satellite/hybrid use Esri; place search uses Photon.
- Preserve Mica/Fluent fallback when transparency is disabled.
- Builds are self-contained for `win-x64` and `win-arm64`. ARM64 uses native
  .NET/WPF; official x64 ExifTool works through Windows 11 emulation.
- Persistent paths:
  - workflows: `%APPDATA%\\Metadata Transfer\\Workflows.json`
  - settings: `%LOCALAPPDATA%\\Metadata Transfer\\Settings.json`
  - WebView2 cache: `%LOCALAPPDATA%\\Metadata Transfer\\WebView2`

### Metadata flow

1. Load files/folders; scan folders recursively (avoiding unsafe reparse-point
   traversal on Windows).
2. Read metadata through ExifTool.
3. Present categorized selected-file values, consolidating aliases such as
   Make/Model.
4. Stage edits, workflows and location coordinates in memory.
5. On **Save Changes**, invoke ExifTool with literal arguments—not a shell.
   Empty editable values remove a tag.
6. Keep backups and per-file results. Transfer mode also preserves modification
   times and copies only writable metadata.

## Build and validation

| Platform | Build | Minimum verification |
| --- | --- | --- |
| macOS | `bash macos/build.command` | Verify `Assets.car`, both slices with `lipo`, and strict code-sign verification. |
| Windows x64 | `.\\windows\\Build.ps1 -Runtime win-x64` | Build verifies ExifTool, publishes self-contained app and runs core tests. |
| Windows ARM64 | `.\\windows\\Build.ps1 -Runtime win-arm64` | Same core checks; confirm native visual/runtime behaviour on ARM hardware. |

For metadata/workflow work, also test disposable JPEG and MP4 files, inspect
ExifTool output and confirm backup behaviour. For map work, test search, click
staging, save, copy/paste, favourites, batch selection and no-network states.
For icon work, inspect small and large Finder/Explorer/taskbar representations.

The x64 Windows workflow includes a native UI smoke-test path. The current
Windows visual treatment still requires manual Windows 11 QA for Mica, title-bar
controls, DPI, file dialogs, shell thumbnails, codecs, WebView2 and keyboard/
mouse behaviour. Do not claim this has been visually verified solely because a
cross-build succeeds.

## Distribution

Current public version: **0.9.0**. The release page is:
<https://github.com/jaisinghdhaliwal/joonga-metadata/releases/tag/v0.9.0>

Published installer assets for that release:

- `Joonga-Metadata-0.9.0-macOS.dmg`: universal drag-to-install DMG containing
  `Joonga Metadata.app` plus an Applications shortcut.
- `Joonga-Metadata-0.9.0-Windows-x64-Setup.exe`: per-user x64 setup.
- `Joonga-Metadata-0.9.0-Windows-arm64-Setup.exe`: per-user ARM64 setup.
- `SHA256SUMS-Installers.txt`: installer SHA-256 hashes.

The DMG was mounted and checked for its app and Applications shortcut. The
Windows installers were checked by extracting their payloads against the
portable builds; actual Windows installation/visual QA remains required.

Important provenance: the existing `v0.9.0` tag predates the universal macOS
build change. The DMG was built from newer source, while Windows setup payloads
package the existing portable `v0.9.0` build. The next release must use a new
version/tag from one exact commit and build all assets from that commit. Never
silently retag an existing release.

The repository CI produces portable ZIP artifacts. It does not yet contain a
fully reproducible DMG/setup-installer release pipeline. macOS artifacts are
ad-hoc signed and not notarized; Windows artifacts are not Authenticode-signed.
Do not describe either as signed, notarized or reputation-clean.

## Release checklist

1. Bump all app/version metadata and create a matching immutable tag.
2. Build from that exact commit on clean environments.
3. Run tests and manual platform QA.
4. Package ZIP/DMG/setup assets and verify SHA-256 hashes.
5. Mount/install each final artifact and launch it.
6. Upload assets and checksums to that exact GitHub Release.
7. Update this document with the version, commit, artifacts, validation and
   signing/notarization status.
