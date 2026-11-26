import AppKit
import SwiftUI

struct PresetSelectionTable: NSViewRepresentable {
    let presets: [Preset]
    @Binding var selection: Set<String>
    let onDelete: () -> Void
    @Environment(\.joongaAccent) private var accent

    func makeCoordinator() -> Coordinator { Coordinator(self) }

    func makeNSView(context: Context) -> NSScrollView {
        let table = PresetTable()
        table.setAccessibilityLabel("Presets")
        table.headerView = nil
        table.backgroundColor = .clear
        table.style = .plain
        table.rowHeight = 46
        table.intercellSpacing = .zero
        table.allowsMultipleSelection = true
        table.addTableColumn(NSTableColumn(identifier: .init("Preset")))
        table.columnAutoresizingStyle = .firstColumnOnlyAutoresizingStyle
        table.delegate = context.coordinator
        table.dataSource = context.coordinator
        let scroll = NSScrollView()
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.documentView = table
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let table = scroll.documentView as? PresetTable else { return }
        context.coordinator.update(self, table: table)
    }

    final class Coordinator: NSObject, NSTableViewDataSource, NSTableViewDelegate {
        private var owner: PresetSelectionTable
        private var syncing = false
        init(_ owner: PresetSelectionTable) { self.owner = owner }

        fileprivate func update(_ newOwner: PresetSelectionTable, table: PresetTable) {
            let reload = owner.presets != newOwner.presets
            owner = newOwner
            table.onDelete = owner.onDelete
            syncing = true
            defer { syncing = false }
            if reload { table.reloadData() }
            restoreSelection(on: table)
            table.enumerateAvailableRowViews { row, _ in
                if let row = row as? AccentMediaRow { row.accent = self.owner.accent; row.refresh() }
            }
        }

        private func restoreSelection(on table: NSTableView) {
            let indices = IndexSet(owner.presets.indices.filter { owner.selection.contains(owner.presets[$0].id) })
            if indices != table.selectedRowIndexes { table.selectRowIndexes(indices, byExtendingSelection: false) }
        }

        func numberOfRows(in tableView: NSTableView) -> Int { owner.presets.count }
        func tableViewSelectionDidChange(_ notification: Notification) {
            guard !syncing, let table = notification.object as? NSTableView else { return }
            owner.selection = Set(table.selectedRowIndexes.compactMap {
                owner.presets.indices.contains($0) ? owner.presets[$0].id : nil
            })
            syncing = true
            restoreSelection(on: table)
            syncing = false
        }
        func tableView(_ tableView: NSTableView, rowViewForRow row: Int) -> NSTableRowView? {
            let view = AccentMediaRow()
            view.accent = owner.accent
            return view
        }
        func tableView(_ tableView: NSTableView, viewFor column: NSTableColumn?, row: Int) -> NSView? {
            PresetCell(preset: owner.presets[row])
        }
    }
}

private final class PresetTable: NSTableView {
    var onDelete: (() -> Void)?
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 51 || event.keyCode == 117 { onDelete?() }
        else { super.keyDown(with: event) }
    }
}

private final class PresetCell: NSTableCellView, AccentSelectionCell {
    private let name: NSTextField
    private let detail: NSTextField
    private let symbol = NSImageView()

    init(preset: Preset) {
        name = NSTextField(labelWithString: preset.name)
        detail = NSTextField(labelWithString: "\(preset.operations.count) assignments")
        super.init(frame: .zero)
        name.font = .systemFont(ofSize: 12, weight: .medium)
        name.lineBreakMode = .byTruncatingTail
        detail.font = .systemFont(ofSize: 10)
        symbol.image = NSImage(systemSymbolName: "slider.horizontal.3", accessibilityDescription: nil)
        let labels = NSStackView(views: [name, detail])
        labels.orientation = .vertical
        labels.alignment = .leading
        labels.spacing = 3
        let contents = NSStackView(views: [symbol, labels])
        contents.alignment = .centerY
        contents.spacing = 10
        contents.translatesAutoresizingMaskIntoConstraints = false
        addSubview(contents)
        NSLayoutConstraint.activate([
            contents.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 16),
            contents.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -16),
            contents.centerYAnchor.constraint(equalTo: centerYAnchor)
        ])
        applySelection(nil)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func applySelection(_ foreground: NSColor?) {
        name.textColor = foreground ?? .labelColor
        detail.textColor = foreground ?? .secondaryLabelColor
        symbol.contentTintColor = foreground ?? .secondaryLabelColor
    }
}
