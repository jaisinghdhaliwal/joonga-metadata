import AppKit
import SwiftUI

struct TransferFileTable: NSViewRepresentable {
    @ObservedObject var library: Library
    let originals: Bool
    @Environment(\.joongaAccent) private var accent

    func makeCoordinator() -> Coordinator { Coordinator(owner: self) }

    func makeNSView(context: Context) -> NSScrollView {
        let table = TranslucentMediaTable()
        table.headerView = nil
        table.backgroundColor = .clear
        table.allowsMultipleSelection = true
        table.rowHeight = 46
        table.intercellSpacing = .zero
        let column = NSTableColumn(identifier: .init("File"))
        column.resizingMask = .autoresizingMask
        table.addTableColumn(column)
        table.columnAutoresizingStyle = .firstColumnOnlyAutoresizingStyle
        table.delegate = context.coordinator
        table.dataSource = context.coordinator
        table.target = context.coordinator
        table.doubleAction = #selector(Coordinator.preview)
        let scroll = NSScrollView()
        scroll.drawsBackground = false
        scroll.hasVerticalScroller = true
        scroll.documentView = table
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let table = scroll.documentView as? NSTableView else { return }
        context.coordinator.update(self, table: table)
    }

    struct Row: Equatable {
        let id: String
        let url: URL?
        let matched: Bool
        let status: String?
    }

    final class Coordinator: NSObject, NSTableViewDataSource, NSTableViewDelegate {
        var owner: TransferFileTable
        var rows: [Row] = []
        var updating = false
        init(owner: TransferFileTable) { self.owner = owner }
        func update(_ owner: TransferFileTable, table: NSTableView) {
            self.owner = owner
            updating = true
            defer { updating = false }
            let library = owner.library
            var next = library.files.map { Row(id: $0.id, url: owner.originals ? $0.original : $0.exported,
                                              matched: $0.original != nil, status: $0.status) }
            if owner.originals {
                next += library.unmatchedOriginals.map { Row(id: $0.path, url: $0, matched: false, status: nil) }
            }
            if next != rows { rows = next; table.reloadData() }
            let ids = owner.originals ? library.originalSelection : library.exportSelection
            let selected = IndexSet(rows.indices.filter { ids.contains(rows[$0].id) })
            if table.selectedRowIndexes != selected && NSEvent.pressedMouseButtons & 1 == 0 {
                table.selectRowIndexes(selected, byExtendingSelection: false)
                if library.activeOriginals == owner.originals, let row = selected.first { table.scrollRowToVisible(row) }
            }
            table.enumerateAvailableRowViews { view, _ in
                guard let row = view as? AccentMediaRow else { return }
                row.accent = owner.accent
                row.refresh()
            }
        }

        func numberOfRows(in tableView: NSTableView) -> Int { rows.count }
        func tableView(_ tableView: NSTableView, shouldSelectRow row: Int) -> Bool {
            !owner.library.busy && rows[row].url != nil
        }
        func tableViewSelectionDidChange(_ notification: Notification) {
            guard !updating, let table = notification.object as? NSTableView else { return }
            owner.library.updateSelection(Set(table.selectedRowIndexes.compactMap { rows[$0].url == nil ? nil : rows[$0].id }),
                                          originals: owner.originals)
        }
        func tableView(_ tableView: NSTableView, rowViewForRow row: Int) -> NSTableRowView? {
            let view = AccentMediaRow()
            view.accent = owner.accent
            return view
        }
        func tableView(_ tableView: NSTableView, viewFor column: NSTableColumn?, row: Int) -> NSView? {
            let item = rows[row]
            return TransferRowHost(rootView: FileListRow(library: owner.library, id: item.id, originals: owner.originals,
                url: item.url, matched: item.matched, status: item.status).environment(\.joongaAccent, owner.accent))
        }
        @objc func preview() { owner.library.quickLook(owner.library.selectedTargets) }
    }
}

private final class TransferRowHost: NSHostingView<AnyView> {
    convenience init<V: View>(rootView: V) { self.init(rootView: AnyView(rootView)) }

    override func hitTest(_ point: NSPoint) -> NSView? {
        // Route primary selection gestures to NSTableView, while leaving row context menus accessible.
        if NSApp.currentEvent?.type == .leftMouseDown && !NSEvent.modifierFlags.contains(.control) { return nil }
        return super.hitTest(point)
    }
}
