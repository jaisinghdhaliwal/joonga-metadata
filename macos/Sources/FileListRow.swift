import AppKit
import SwiftUI

struct FileListRow: View {
    @ObservedObject var library: Library
    let id: String
    let originals: Bool
    let url: URL?
    let matched: Bool
    let status: String?

    @Environment(\.joongaAccent) private var accent
    @Environment(\.controlActiveState) private var controlActiveState

    private var isSelected: Bool {
        selection.contains(id)
    }

    private var selection: Set<String> {
        originals ? library.originalSelection : library.exportSelection
    }


    private var isEmphasized: Bool {
        controlActiveState == .key && library.activeOriginals == originals
    }

    private var rowText: Color { isSelected ? accent.selectionText(active: isEmphasized) : .primary }
    private var detailText: Color { isSelected ? rowText.opacity(0.86) : .secondary }
    private var detail: String? { status.map { url?.deletingLastPathComponent().lastPathComponent ?? $0 } }

    var body: some View {
        HStack(spacing: 10) {
            Thumbnail(url: url, quickLookRow: QuickLookRow(id: id, originals: originals), contentMode: .fill)
                .frame(width: 42, height: 34)
                .clipShape(RoundedRectangle(cornerRadius: 4, style: .continuous))
            VStack(alignment: .leading, spacing: 2) {
                Text(url?.lastPathComponent ?? "No matching original")
                    .font(.system(size: 12.5, weight: .medium))
                    .foregroundStyle(rowText)
                    .lineLimit(1)
                    .truncationMode(.middle)
                if let detail {
                    Text(detail)
                        .font(.system(size: 10.5))
                        .foregroundStyle(detailText)
                        .lineLimit(1)
                }
            }
            Spacer(minLength: 0)
            if !originals, let status {
                Image(systemName: matched ? "checkmark.circle.fill" : "exclamationmark.circle")
                    .font(.system(size: 12))
                    .foregroundStyle(isSelected ? rowText : matched ? Color.green : Color.orange)
                    .accessibilityLabel(status)
                    .help(status)
            }
        }
        .frame(height: 46)
        .id(id)
        .tag(id)
        .contextMenu { FileContextMenu(library: library, id: id, originals: originals, url: url) }
        .padding(.horizontal, 14)
    }

}
