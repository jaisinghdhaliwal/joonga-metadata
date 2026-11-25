import AppKit
import SwiftUI
import Quartz

final class FilePreviewWindow: NSWindow, QLPreviewPanelDataSource, QLPreviewPanelDelegate {
    private var previewURLs = [URL]()
    var selectedFiles: (() -> [URL])?
    var selectedRow: (() -> QuickLookRow?)?
    var selectedURL: (() -> URL?)?
    private var previewRow: QuickLookRow?
    private let thumbnails = NSHashTable<ThumbnailAnchorView>.weakObjects()

    func registerThumbnail(_ thumbnail: ThumbnailAnchorView) { thumbnails.add(thumbnail) }
    private var keyboardMonitor: Any?

    func enableQuickLook() {
        keyboardMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard let self, event.keyCode == 49,
                  event.modifierFlags.intersection([.command, .option, .control]).isEmpty else { return event }
            if QLPreviewPanel.sharedPreviewPanelExists(), let panel = QLPreviewPanel.shared(), event.window === panel {
                panel.orderOut(nil)
                return nil
            }
            guard event.window === self, self.attachedSheet == nil,
                  !(self.firstResponder is NSTextView), !(self.firstResponder is NSTextField) else { return event }
            let urls = self.selectedFiles?() ?? []
            guard !urls.isEmpty else { return event }
            self.showQuickLook(urls)
            return nil
        }
    }

    func showQuickLook(_ urls: [URL], from row: QuickLookRow? = nil, firstURL: URL? = nil) {
        guard !urls.isEmpty, let panel = QLPreviewPanel.shared() else { return }
        previewRow = row ?? selectedRow?()
        let focusURL = firstURL ?? selectedURL?()
        if let focusURL, urls.contains(focusURL) {
            previewURLs = [focusURL] + urls.filter { $0 != focusURL }
        } else {
            previewURLs = urls
        }
        panel.dataSource = self
        panel.delegate = self
        panel.reloadData()
        panel.currentPreviewItemIndex = 0
        panel.makeKeyAndOrderFront(nil)
    }

    private func transitionThumbnail(for item: QLPreviewItem?) -> ThumbnailAnchorView? {
        guard !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion,
              let url = item?.previewItemURL else { return nil }
        let available = thumbnails.allObjects.filter {
            $0.window === self && $0.url == url && !$0.screenFrame.isEmpty
        }
        return available.first { $0.row == previewRow }
            ?? available.first { $0.row?.originals == previewRow?.originals }
            ?? available.first
    }

    func previewPanel(_ panel: QLPreviewPanel!, sourceFrameOnScreenFor item: QLPreviewItem!) -> NSRect {
        transitionThumbnail(for: item)?.screenFrame ?? .zero
    }

    func previewPanel(_ panel: QLPreviewPanel!, transitionImageFor item: QLPreviewItem!,
                      contentRect: UnsafeMutablePointer<NSRect>!) -> Any! {
        guard let image = transitionThumbnail(for: item)?.image else { return nil }
        contentRect?.pointee = NSRect(origin: .zero, size: image.size)
        return image
    }

    override func acceptsPreviewPanelControl(_ panel: QLPreviewPanel!) -> Bool { true }
    override func beginPreviewPanelControl(_ panel: QLPreviewPanel!) {
        panel.dataSource = self
        panel.delegate = self
    }
    override func endPreviewPanelControl(_ panel: QLPreviewPanel!) {
        panel.dataSource = nil
        panel.delegate = nil
    }
    func numberOfPreviewItems(in panel: QLPreviewPanel!) -> Int { previewURLs.count }
    func previewPanel(_ panel: QLPreviewPanel!, previewItemAt index: Int) -> QLPreviewItem! { previewURLs[index] as NSURL }

    deinit { if let keyboardMonitor { NSEvent.removeMonitor(keyboardMonitor) } }
}

extension Library {
    func contextTargets(id: String, originals: Bool, fallback: URL?) -> [URL] {
        let ids = originals ? originalSelection : exportSelection
        if ids.contains(id) && activeOriginals == originals { return selectedTargets }
        return fallback.map { [$0] } ?? []
    }

    func quickLook(_ urls: [URL], from row: QuickLookRow? = nil, firstURL: URL? = nil) {
        let window = NSApp.windows.compactMap { $0 as? FilePreviewWindow }.first
        window?.showQuickLook(urls, from: row, firstURL: firstURL)
    }
}

struct FileContextMenu: View {
    @ObservedObject var library: Library
    let id: String
    let originals: Bool
    let url: URL?

    private var targets: [URL] { library.contextTargets(id: id, originals: originals, fallback: url) }

    var body: some View {
        Button("Quick Look") { library.quickLook(targets, from: QuickLookRow(id: id, originals: originals), firstURL: url) }.disabled(targets.isEmpty)
        Button("Open") { for url in targets { NSWorkspace.shared.open(url) } }.disabled(targets.isEmpty)
        Button("Reveal in Finder") { NSWorkspace.shared.activateFileViewerSelecting(targets) }.disabled(targets.isEmpty)
        Divider()
        Button("Copy Path") {
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(targets.map(\.path).joined(separator: "\n"), forType: .string)
        }.disabled(targets.isEmpty)
        Button("Copy Location") {
            guard targets.count == 1, let target = targets.first,
                  let point = library.location(for: target.path) else { return }
            NSPasteboard.general.clearContents()
            NSPasteboard.general.setString(point.text, forType: .string)
        }.disabled(targets.count != 1 || targets.first.flatMap { library.location(for: $0.path) } == nil)
        Button("Paste Location") {
            if let point = library.pastedLocation() {
                library.stageLocation(point, targets: targets)
            }
        }.disabled(targets.isEmpty || library.busy || library.exif == nil)
        Button("Apply Workflow…") {
            let ids = originals ? library.originalSelection : library.exportSelection
            if !ids.contains(id) || library.activeOriginals != originals { library.updateSelection([id], originals: originals) }
            library.showWorkflows = true
        }.disabled(targets.isEmpty || library.busy)
    }
}
