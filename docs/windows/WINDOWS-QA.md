# Windows runtime acceptance checks

Cross-compilation and engine tests have run on macOS. The following checks are provided for a Windows 11 machine; they are not marked passed without executing them there.

1. Extract a release ZIP, run `Joonga Metadata.exe --smoke-test` from PowerShell and inspect `SmokeResults`. Exit code must be 0. Screenshots capture WPF content, not the compositor's Mica effect.
2. Launch normally in light/dark/high-contrast themes. Check Mica with transparency enabled, native snap layouts, focused/unfocused states, 100/150/200% DPI, minimum window size, and moving between monitors.
3. Drop a source folder and export folder. Try nested folders, -HDR exports, ambiguous stems, Unicode/long paths, loose files and mixed image/video formats. Confirm green match markers, continuing alternating stripes, and that the drop outline clears when you leave a pane. Clear one pane and check its Choose Folder action.
4. Ctrl/Shift-select files. Check one Make/Model field, orange mixed values, editable missing fields and no imported-original values masquerading as selected-file values. Save changes on disposable copies; inspect ExifTool output and retained backups.
5. Switch selections quickly and check that the preview title, thumbnail and metadata refer to the current selection. Select files with different drafts and check that their shared field reports differing pending edits. Revert must affect only the current selection. Close with pending drafts and verify the discard prompt.
6. Apply a workflow to selected files. Confirm camera aliases through ExifTool read-back. Try an unsupported tag and confirm warnings remain visible and failed drafts are retained.
7. Create, duplicate, rename, delete, import several workflow JSON libraries and export a multi-selection. Check Ctrl+C/V/X/A/Z/Y, Ctrl+N/S, invalid-line feedback and save/discard/cancel prompts. Import the exported JSON into the Mac app.
8. Save a default exports folder; close/reopen and confirm restoration. Close the main window and confirm the process exits.
9. Use right-click actions, Space preview, previous/next, image thumbnails and video Play/Pause. Try a codec unsupported by Windows and check the external-player fallback.
10. Open Map with disposable files that lack GPS and stage pins on different selections. Verify no file is modified before Save changes, then save both together. Verify the pin and backup with ExifTool. Repeat with multi-selection and a replacement pin in the southern or western hemisphere. Search for a city and an address, choose a result, and check that the map centres and places the pin. Refine it with a click before saving. Check saved pins, favourites, copy/paste, all three styles, empty results, and offline/missing-WebView2 feedback.
11. Copy a batch, press Cancel, and verify the current file finishes and later files are reported as unprocessed. Attempt to close during a write; it must remain open until the operation finishes/cancels.
12. Run both x64 and ARM64 packages on their appropriate hardware. ARM64 must also successfully execute the bundled x64 ExifTool through Windows' emulator.

Do not run destructive or metadata-write tests against irreplaceable originals. The synthetic Fixtures folder is suitable for initial checks.

For the single-row title bar, verify that Windows draws the caption buttons with readable glyphs in light and dark mode. Check minimise, maximise, restore, close, caption dragging, double-click, Alt+Space, and Snap Layouts on hover. Check restored and maximised windows at 100%, 150%, and 200% scaling, including across monitors. In high contrast, check that the standard title bar appears above the command row and all commands remain usable.
