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
| Metadata | Browse, sort, and edit categorized metadata using the shared file pane and a collapsible right inspector sidebar. |
| Map | Plot saved/pending GPS data with grouped cluster counts, search/click a place, stage coordinates, copy/paste locations and manage favourites using the shared file pane. |
| Transfer | Match originals to exports and copy writable metadata to matching export files. |

Matching is case-insensitive by filename stem across extensions. A trailing
`-HDR` suffix may match an unsuffixed original. Skip ambiguous and same-path
matches. Folder scans are recursive; individual files and folders can be
dropped into either pane.

### Design direction

File table headers (`30pt` tall, vertically centered text) and alternating
stripes (`46pt` row height, `8pt` horizontal inset, `6pt` corner radius) use a
translucent dark tint over the macOS backdrop. Keep the same stripe pattern
across all modes, including borderless empty-state drop areas. Map controls use
separate native Liquid Glass pills/buttons (`GlassEffectContainer`,
`.glassEffect`, and an accent-tinted `36pt` Search pill) on macOS 26 and system
material on earlier versions, with Favourites in the window toolbar and
Copy/Paste Location in file context menus and the Edit menu. Open Settings from
the app menu or Command-comma, and open or toggle the inspector with Command-I.

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
  directory in the worktree (`macos/build.command` selects the newest
  `AppIcon*.icon` containing `icon.json`). `AppIcon.icns` is only a static
  compatibility fallback; do not delete, rename or revert an Icon Composer
  source without checking the current worktree and its associated artwork.
- ExifTool 13.64 (`Image-ExifTool-13.64.tar.gz`, SHA-256 verified) is bundled
  into `Contents/Resources/exiftool/` during `macos/build.command` (overridable
  via `EXIFTOOL_DIR`). The app defaults to the bundled ExifTool (falling back
  to `/opt/homebrew/bin/exiftool`, `/usr/local/bin/exiftool`, `/usr/bin/exiftool`
  only if unbundled). Settings allows configuring a custom ExifTool path
  (stored in Preferences as `CustomExifToolPath`, validated with `-ver`) and
  restoring the app-bundled ExifTool with one button.
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
| macOS | `bash macos/build.command` | Verify `Assets.car`, bundled `Contents/Resources/exiftool/exiftool`, both slices with `lipo`, and strict code-sign verification. |
| Windows x64 | `.\\windows\\Build.ps1 -Runtime win-x64` | Build verifies ExifTool, publishes self-contained app and runs core tests. |
| Windows ARM64 | `.\\windows\\Build.ps1 -Runtime win-arm64` | Same core checks; confirm native visual/runtime behaviour on ARM hardware. |

### Current state (2026-11-25)

- The map star adds a favourite and its arrow opens the saved list, including removal.
  Cluster counts sit at the top right without a border. Map search uses an icon.
  Metadata opens with a compact inspector. The pending count opens a list of
  affected filenames, and the file grid marks edits beside the filename.

- Functional parity across platforms is a product requirement. Use the
  platform's native UI conventions for equivalent controls and compare both
  implementations during feature work.
- Windows now shares the Metadata/Map file grid and selection, retains its
  WebView2 map between modes, and has a resizable shared pane, collapsible
  thumbnail inspector, reorderable per-mode column sets, file removal and
  clear actions. Batches of 60 show progressive file metadata and GPS reads.
  Map options and toolbar favourites complement location commands in file
  context menus and Ctrl+Shift+C/V. Nearby photo pins group with count badges
  and support zooming and individual or group selection. ExifTool Settings
  validates a typed or chosen custom path and can restore the bundled tool.
- Windows parity changes were cross-built on macOS with .NET 10. The core
  regression runner passed 42 checks with ExifTool, including partial batch
  reads of JPEG and MP4 GPS data. A headless Chromium test with a simulated
  WebView2 bridge exercised clustered pins, zoom, file selection and map
  clicks. Native Windows testing of the splitter, Mica, Shell thumbnails and
  WebView2 is still open. The native smoke test now covers Metadata loading,
  inspector collapse and file removal.

- `macos/build.command` bundles pinned ExifTool 13.64 into
  `Contents/Resources/exiftool/` and compiles the newest valid
  `AppIcon*.icon` directory (`macos/AppIcon 7.icon` in the latest build).
- macOS Settings (**Tools and data → ExifTool**) supports entering or choosing
  a custom ExifTool executable path, validating it with `-ver`, and restoring
  the app-bundled ExifTool via **Use Bundled ExifTool**. Settings is opened via
  the app menu or `⌘,` (no window toolbar button).
- macOS UI merges `Preview` and `Metadata` into a unified `Metadata` workspace
  with a collapsible right inspector (Finder-style `210pt` preview thumbnail,
  title/kind-size header, inline section jumper, flat virtualized `LazyVStack`
  rows, and toolbar/`⌘I` collapse toggle), sharing a single persistent
  `MediaTable` file pane
  and a `ZStack`-retained `MKMapView` (`WorkspaceMap`) across `Metadata` and
  `Map` inside one `HSplitView` alongside `Transfer`. Translucent `30pt` table
  headers with vertically centered text, MetaImage-style `Filename` rows
  (`42×34` filled thumbnails, abbreviated folder path, `46pt` row height),
  alternating checkerboard stripes (`8pt` inset, `6pt` radius), and borderless
  empty drop prompts sit over stable `.underWindowBackground` vibrancy extending
  into the transparent unified titlebar.
- macOS media tables (`Metadata`, `Map`) load folders immediately as
  placeholder rows with a `2.5pt` accent loading progress bar along the top
  header edge and stream ExifTool metadata in batches of 60 via `readBatch`
  (`runJSON` isolates `stdout` from `stderr` with single-pass GPS extraction
  and precomputed `MediaItem` fields). `MediaTable` uses version-gated sorting/
  reloading (`mediaItemsVersion`/`pendingVersion`) and in-place column
  preservation across mode switches; media tables and map pins use pure AppKit
  reusable views (`MediaTableCell`/`MediaThumbnailView`,
  `PhotoMapAnnotationView`, and `PhotoClusterAnnotationView` for grouped map
  thumbnails with a corner count badge) alongside async multi-file inspection
  to keep workspace switching smooth, support per-mode customizable and
  reorderable columns (including Road/Location, City, State/Region, Country,
  Altitude, Camera/Exposure fields, or Filename-only) persisted in Preferences,
  and support Clear All Files (`trash` toolbar button + File menu) and
  right-click / File-menu removal of selected files.
- macOS window chrome hides the visible titlebar title (`titleVisibility =
  .hidden`), aligns file-pane toolbar controls on the left next to the traffic
  lights (`originals`, `clear`, `exports`, `refresh`), keeps Previous/Next File
  navigation in the View menu (`⌥⌘[` / `⌥⌘]`), centers the workspace switcher
  (`workspace`), and groups right-pane toolbar controls on the right
  (`workflows`, `favourites`, `inspector`).
- macOS Map workspace separates a `36pt` search field pill, an accent-tinted
  `36pt` Liquid Glass Search button, and a `36pt` Map Options pill inside
  `GlassEffectContainer` (macOS 26+ with `.regularMaterial` fallback), moves
  Favourites into an `NSMenuToolbarItem` in the right toolbar group, moves
  Copy/Paste Location into file context menus (and Edit menu shortcuts `⇧⌘C` /
  `⇧⌘V` across modes), and maps `⌘I` to toggle the collapsible
  inspector/preview across workspaces.
- Verified on macOS (`0.9.1`, commit `81037b8`): `bash macos/build.command`
  succeeded, `Assets.car` is non-empty, bundled ExifTool reported `13.64`,
  `lipo -archs` confirmed `x86_64` and `arm64`, `codesign --verify --strict
  --all-architectures` passed, and `Info.plist` reports `0.9.1`. Interactive
  GUI visual QA of the unified `Metadata`/`Map` shared file pane, Finder-style
  inspector, toolbar layout (without prev/next toolbar buttons), and clustered
  map pin badges remains open.

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

Use GitHub CLI for release maintenance from the terminal. If GitHub CLI has
no separate login, use the existing Git credential helper to supply
`GH_TOKEN` for the command process. Keep credentials out of logs and files.
Read access to the current release and repository push permission were
verified with this setup on 2026-11-25.

Current public version: **0.9.1** (`v0.9.1`, commit
`81037b8d4c876f25986f959f00d469168e2bdf1e`). The release page is:
<https://github.com/jaisinghdhaliwal/joonga-metadata/releases/tag/v0.9.1>

Published assets for that release (all built from commit
`81037b8d4c876f25986f959f00d469168e2bdf1e`):

- `Joonga-Metadata-0.9.1-macOS.dmg`: universal (`arm64` + `x86_64`)
  drag-to-install DMG containing `Joonga Metadata.app` plus an Applications
  shortcut.
- `Joonga-Metadata-0.9.1-macOS.zip`: universal (`arm64` + `x86_64`) portable
  app bundle archive.
- `Joonga-Metadata-0.9.1-Windows-x64.zip`: self-contained Windows 11 x64
  portable build from CI run `45657484866` (core tests + `--smoke-test` UI
  check passed on CI).
- `Joonga-Metadata-0.9.1-Windows-arm64.zip`: self-contained Windows 11 ARM64
  portable build from CI run `45657484866` (core tests passed on CI).
- `SHA256SUMS.txt`: SHA-256 checksums for all release archives.

Validation status for `v0.9.1`: macOS `bash macos/build.command`, `Assets.car`,
bundled ExifTool `13.64`, `lipo -archs` (`x86_64` + `arm64`), strict
`codesign`, `CFBundleShortVersionString` `0.9.1`, DMG mount/contents check via
`diskutil image attach`, and `shasum -a 256 -c SHA256SUMS.txt` all passed, along
with macOS (`45657484917`) and Windows (`45657484866`) CI workflows. Interactive
macOS GUI visual QA and manual Windows 11 QA (including Windows installer
EXEs, which were not built on macOS) remain open.

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
