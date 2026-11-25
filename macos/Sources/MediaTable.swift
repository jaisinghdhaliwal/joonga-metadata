import AppKit
import SwiftUI

struct MediaTable: NSViewRepresentable {
    @ObservedObject var library: Library
    let compact: Bool
    @Environment(\.joongaAccent) private var accent

    func makeCoordinator() -> Coordinator { Coordinator(library: library, accent: accent, compact: compact) }

    func makeNSView(context: Context) -> NSScrollView {
        let table = NSTableView()
        table.headerView = NSTableHeaderView()
        table.style = .inset
        table.allowsMultipleSelection = true
        table.allowsEmptySelection = true
        table.usesAlternatingRowBackgroundColors = true
        table.rowHeight = 42
        table.intercellSpacing = NSSize(width: 8, height: 0)
        table.columnAutoresizingStyle = .firstColumnOnlyAutoresizingStyle
        table.setAccessibilityLabel(compact ? "Loaded files" : "File metadata")
        table.dataSource = context.coordinator
        table.delegate = context.coordinator
        table.target = context.coordinator
        table.doubleAction = #selector(Coordinator.preview(_:))
        table.menu = context.coordinator.fileMenu
        let keys: [Column] = compact ? [.file] : Column.allCases
        for field in keys {
            let col = NSTableColumn(identifier: .init(field.rawValue))
            col.title = field.rawValue
            col.minWidth = field == .file ? 165 : 45
            col.width = field.width
            col.sortDescriptorPrototype = NSSortDescriptor(key: field.rawValue, ascending: true)
            table.addTableColumn(col)
        }
        table.sortDescriptors = [NSSortDescriptor(key: Column.file.rawValue, ascending: true)]
        let scroll = NSScrollView()
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = !compact
        scroll.documentView = table
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let table = scroll.documentView as? NSTableView else { return }
        context.coordinator.accent = accent
        context.coordinator.update(table)
    }

    enum Column: String, CaseIterable {
        case file = "File", tags = "Tags", make = "Camera Make", date = "Capture Date", gps = "GPS Coordinates", status = "Status"
        var width: CGFloat {
            switch self {
            case .file: return 330
            case .tags: return 62
            case .make: return 135
            case .date: return 163
            case .gps: return 176
            case .status: return 76
            }
        }
    }

    final class Coordinator: NSObject, NSTableViewDataSource, NSTableViewDelegate, NSMenuDelegate {
        let library: Library
        var accent: MacAccent
        let compact: Bool
        private var updating = false
        private var rows: [MediaItem] = []
        private var snapshot: [String] = []
        private weak var currentTable: NSTableView?
        lazy var fileMenu: NSMenu = {
            let menu = NSMenu()
            menu.delegate = self
            menu.autoenablesItems = false
            return menu
        }()

        init(library: Library, accent: MacAccent, compact: Bool) {
            self.library = library
            self.accent = accent
            self.compact = compact
        }

        func numberOfRows(in tableView: NSTableView) -> Int { rows.count }

        func update(_ table: NSTableView) {
            currentTable = table
            let sort = table.sortDescriptors.first
            let field = Column(rawValue: sort?.key ?? "") ?? .file
            rows = library.mediaItems.sorted { left, right in
                let result = compare(left, right, by: field)
                if result == .orderedSame { return left.id.localizedStandardCompare(right.id) == .orderedAscending }
                return sort?.ascending == false ? result == .orderedDescending : result == .orderedAscending
            }
            library.displayedMediaOrder = rows.map(\.id)
            let newSnapshot = rows.map { item in
                [item.id, item.make, item.captureDate, String(item.tagCount),
                 point(item)?.text ?? "", library.hasPending(item.id) ? "1" : "0", library.mode.rawValue].joined(separator: "\u{0}")
            }
            updating = true
            if snapshot != newSnapshot {
                snapshot = newSnapshot
                table.reloadData()
            }
            let indices = IndexSet(rows.indices.filter { library.mediaSelection.contains(rows[$0].id) })
            if indices != table.selectedRowIndexes { table.selectRowIndexes(indices, byExtendingSelection: false) }
            table.enumerateAvailableRowViews { row, _ in
                if let row = row as? AccentMediaRow { row.accent = self.accent; row.refresh() }
            }
            updating = false
        }

        func tableView(_ tableView: NSTableView, sortDescriptorsDidChange oldDescriptors: [NSSortDescriptor]) {
            update(tableView)
        }

        func tableViewSelectionDidChange(_ notification: Notification) {
            guard !updating, let table = notification.object as? NSTableView else { return }
            library.mediaSelection = Set(table.selectedRowIndexes.compactMap { rows.indices.contains($0) ? rows[$0].id : nil })
        }

        func tableView(_ tableView: NSTableView, shouldSelectRow row: Int) -> Bool {
            !library.busy
        }

        func tableView(_ tableView: NSTableView, rowViewForRow row: Int) -> NSTableRowView? {
            let view = AccentMediaRow()
            view.accent = accent
            return view
        }

        func tableView(_ tableView: NSTableView, viewFor tableColumn: NSTableColumn?, row: Int) -> NSView? {
            guard let col = tableColumn, let field = Column(rawValue: col.identifier.rawValue), rows.indices.contains(row) else { return nil }
            let item = rows[row]
            let cell = MediaTableCell()
            cell.toolTip = item.id
            switch field {
            case .file:
                let detail = library.mode == .map ? point(item)?.text ?? "No location" : item.url.deletingLastPathComponent().path
                cell.configure(value: item.name, detail: detail, thumbnail: item, pending: compact && library.hasPending(item.id))
            case .status:
                cell.configure(value: library.hasPending(item.id) ? "Pending" : "", pending: library.hasPending(item.id))
            default:
                let text = value(item, field)
                cell.configure(value: text.isEmpty ? "—" : text)
            }
            return cell
        }

        @objc func preview(_ table: NSTableView) {
            guard rows.indices.contains(table.clickedRow) else { return }
            let item = rows[table.clickedRow]
            library.quickLook([item.url], from: QuickLookRow(id: item.id, originals: false))
        }

        func menuNeedsUpdate(_ menu: NSMenu) {
            menu.removeAllItems()
            guard let table = currentTable else { return }
            if rows.indices.contains(table.clickedRow), !table.selectedRowIndexes.contains(table.clickedRow) {
                table.selectRowIndexes(IndexSet(integer: table.clickedRow), byExtendingSelection: false)
            }
            guard !library.selectedTargets.isEmpty else { return }
            for (title, action) in [
                ("Quick Look", #selector(previewSelection)),
                ("Reveal in Finder", #selector(revealSelection)),
                ("Stage Workflow…", #selector(stageWorkflow)),
            ] {
                let item = menu.addItem(withTitle: title, action: action, keyEquivalent: "")
                item.target = self
                item.isEnabled = !library.busy
            }
        }

        @objc private func previewSelection() { library.quickLook(library.selectedTargets) }
        @objc private func revealSelection() { NSWorkspace.shared.activateFileViewerSelecting(library.selectedTargets) }
        @objc private func stageWorkflow() { library.showWorkflows = true }

        private func point(_ item: MediaItem) -> MapCoordinate? {
            library.locationDrafts[item.id] ?? item.pin.map { MapCoordinate($0.coordinate) }
        }

        private func value(_ item: MediaItem, _ field: Column) -> String {
            switch field {
            case .file: return item.name
            case .tags: return String(item.tagCount)
            case .make: return item.make
            case .date: return item.captureDate
            case .gps: return point(item)?.text ?? ""
            case .status: return library.hasPending(item.id) ? "Pending" : ""
            }
        }

        private func compare(_ left: MediaItem, _ right: MediaItem, by field: Column) -> ComparisonResult {
            if field == .gps, let a = point(left), let b = point(right) {
                if a.latitude != b.latitude { return a.latitude < b.latitude ? .orderedAscending : .orderedDescending }
                if a.longitude != b.longitude { return a.longitude < b.longitude ? .orderedAscending : .orderedDescending }
                return .orderedSame
            }
            return value(left, field).localizedStandardCompare(value(right, field))
        }
    }
}

private final class AccentMediaRow: NSTableRowView {
    var accent = MacAccent()

    override var isSelected: Bool { didSet { refresh() } }
    override var isEmphasized: Bool { didSet { refresh() } }
    override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); refresh() }
    override func viewDidChangeEffectiveAppearance() { super.viewDidChangeEffectiveAppearance(); refresh() }
    override func didAddSubview(_ subview: NSView) { super.didAddSubview(subview); refresh() }

    private var usesEmphasis: Bool { isEmphasized || window?.isKeyWindow == true }

    func refresh() {
        let foreground = isSelected ? accent.selectionForeground(active: usesEmphasis) : nil
        for case let cell as MediaTableCell in subviews { cell.applySelection(foreground) }
        needsDisplay = true
    }

    override func drawSelection(in dirtyRect: NSRect) {
        guard isSelected else { return }
        accent.selectionBackground(active: usesEmphasis).setFill()
        NSBezierPath(roundedRect: bounds.insetBy(dx: 1, dy: 3), xRadius: 6, yRadius: 6).fill()
    }
}

private final class MediaTableCell: NSTableCellView {
    private let titleLabel = NSTextField(labelWithString: "")
    private var detailLabel: NSTextField?
    private var pendingIcon: NSImageView?

    func configure(value: String, detail: String? = nil, thumbnail: MediaItem? = nil, pending: Bool = false) {
        titleLabel.stringValue = value
        titleLabel.font = thumbnail == nil ? .systemFont(ofSize: 12) : .systemFont(ofSize: 12, weight: .medium)
        titleLabel.lineBreakMode = .byTruncatingMiddle
        titleLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
        var labels: [NSView] = [titleLabel]
        if let detail {
            let label = NSTextField(labelWithString: detail)
            label.font = .systemFont(ofSize: 10)
            label.lineBreakMode = .byTruncatingMiddle
            label.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
            detailLabel = label
            labels.append(label)
        }
        let textStack = NSStackView(views: labels)
        textStack.orientation = .vertical
        textStack.alignment = .leading
        textStack.spacing = 2
        var views: [NSView] = []
        if let thumbnail {
            let image = NSHostingView(rootView: Thumbnail(url: thumbnail.url,
                quickLookRow: QuickLookRow(id: thumbnail.id, originals: false)).frame(width: 44, height: 31))
            image.widthAnchor.constraint(equalToConstant: 44).isActive = true
            image.heightAnchor.constraint(equalToConstant: 31).isActive = true
            views.append(image)
        }
        views.append(textStack)
        if pending, let symbol = NSImage(systemSymbolName: "circle.fill", accessibilityDescription: nil)?
            .withSymbolConfiguration(.init(pointSize: 6, weight: .regular)) {
            let icon = NSImageView(image: symbol)
            pendingIcon = icon
            views.append(icon)
        }
        let contents = NSStackView(views: views)
        contents.spacing = 9
        contents.alignment = .centerY
        contents.translatesAutoresizingMaskIntoConstraints = false
        addSubview(contents)
        NSLayoutConstraint.activate([
            contents.leadingAnchor.constraint(equalTo: leadingAnchor, constant: 5),
            contents.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -4),
            contents.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
        setAccessibilityLabel([value, detail, pending ? "Pending changes" : nil].compactMap { $0 }.joined(separator: ", "))
        applySelection(nil)
    }

    func applySelection(_ foreground: NSColor?) {
        titleLabel.textColor = foreground ?? .labelColor
        detailLabel?.textColor = foreground ?? .secondaryLabelColor
        pendingIcon?.contentTintColor = foreground ?? .systemOrange
    }
}
