import AppKit
import SwiftUI

struct FileListRow: View {
    @ObservedObject var library: Library
    let id: String
    let originals: Bool
    let url: URL?
    let index: Int
    let matched: Bool
    let status: String?

    @Environment(\.joongaAccent) private var accent
    @Environment(\.controlActiveState) private var controlActiveState

    private var isSelected: Bool {
        (originals ? library.originalSelection : library.exportSelection).contains(id)
    }

    private var isEmphasized: Bool {
        controlActiveState == .key && library.activeOriginals == originals
    }

    private var rowText: Color { isSelected ? accent.selectionText(active: isEmphasized) : .primary }
    private var detailText: Color { isSelected ? rowText.opacity(0.86) : .secondary }
    private var detail: String? { status.map { url?.deletingLastPathComponent().lastPathComponent ?? $0 } }

    var body: some View {
        HStack(spacing: 10) {
            Thumbnail(url: url, quickLookRow: QuickLookRow(id: id, originals: originals))
                .frame(width: 60, height: 34)
            VStack(alignment: .leading, spacing: 2) {
                Text(url?.lastPathComponent ?? "No matching original")
                    .font(.system(size: 12, weight: .medium))
                    .foregroundStyle(rowText)
                    .lineLimit(1)
                    .truncationMode(.middle)
                if let detail {
                    Text(detail)
                        .font(.system(size: 10))
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
        .frame(height: 42)
        .id(id)
        .tag(id)
        .contextMenu { FileContextMenu(library: library, id: id, originals: originals, url: url) }
        .listRowInsets(EdgeInsets(top: 0, leading: 14, bottom: 0, trailing: 14))
        .listRowSeparator(.hidden)
        .listRowBackground(rowBackground)
    }

    private var rowBackground: some View {
        let shape = RoundedRectangle(cornerRadius: 5, style: .continuous)
        return ZStack(alignment: .leading) {
            Color(nsColor: .controlBackgroundColor)
            if isSelected {
                shape.fill(accent.selectionFill(active: isEmphasized))
            } else {
                shape.fill(index.isMultiple(of: 2) ? Color.primary.opacity(0.045) : .clear)
                if matched {
                    shape.fill(Color.green.opacity(0.045))
                    Capsule().fill(Color.green.opacity(0.72)).frame(width: 2).padding(.vertical, 9)
                }
            }
        }
        .padding(.horizontal, 10)
        .background(Color(nsColor: .controlBackgroundColor))
    }
}
