import AppKit
import SwiftUI

struct QuickLookRow: Equatable {
    let id: String
    let originals: Bool
}

/// The AppKit anchor stays inside the actual SwiftUI thumbnail and moves with the list.
struct QuickLookThumbnail: NSViewRepresentable {
    let row: QuickLookRow
    let url: URL
    let image: NSImage

    func makeNSView(context: Context) -> ThumbnailAnchorView { ThumbnailAnchorView() }

    func updateNSView(_ view: ThumbnailAnchorView, context: Context) {
        view.row = row
        view.url = url
        view.image = image
    }
}

final class ThumbnailAnchorView: NSView {
    var row: QuickLookRow?
    var url: URL?
    var image: NSImage?

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        (window as? FilePreviewWindow)?.registerThumbnail(self)
    }

    override func hitTest(_ point: NSPoint) -> NSView? { nil }

    var screenFrame: NSRect {
        guard let window, window.isVisible, !isHiddenOrHasHiddenAncestor,
              !bounds.isEmpty else { return .zero }

        var visible = visibleRect
        if let clip = enclosingScrollView?.contentView {
            visible = visible.intersection(convert(clip.bounds, from: clip))
        }
        // A clipped or recycled row should use Quick Look’s regular fade.
        guard !visible.isNull, visible.contains(bounds) else { return .zero }
        let frame = window.convertToScreen(convert(bounds, to: nil))
        guard NSScreen.screens.contains(where: { $0.frame.intersects(frame) }) else { return .zero }
        return frame
    }
}
