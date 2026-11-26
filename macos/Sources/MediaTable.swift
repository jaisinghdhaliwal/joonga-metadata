import AppKit
import SwiftUI

struct MediaTable: NSViewRepresentable {
    @ObservedObject var library: Library
    let mode: WorkspaceMode
    @Environment(\.joongaAccent) private var accent

    func makeCoordinator() -> Coordinator { Coordinator(library: library, accent: accent, mode: mode) }

    func makeNSView(context: Context) -> NSScrollView {
        let table = TranslucentMediaTable()
        let header = TranslucentMediaHeaderView()
        header.menu = context.coordinator.headerMenu
        table.headerView = header
        table.style = .plain
        table.backgroundColor = .clear
        table.allowsMultipleSelection = true
        table.allowsEmptySelection = true
        table.usesAlternatingRowBackgroundColors = false
        table.allowsColumnReordering = true
        table.allowsColumnResizing = true
        table.rowHeight = 46
        table.intercellSpacing = NSSize(width: 0, height: 0)
        table.setAccessibilityLabel("Loaded files")
        table.dataSource = context.coordinator
        table.delegate = context.coordinator
        table.target = context.coordinator
        table.doubleAction = #selector(Coordinator.preview(_:))
        table.menu = context.coordinator.fileMenu
        context.coordinator.syncColumns(on: table, mode: mode)
        table.sortDescriptors = [NSSortDescriptor(key: Column.file.rawValue, ascending: true)]
        let scroll = NSScrollView()
        scroll.drawsBackground = false
        scroll.automaticallyAdjustsContentInsets = false
        scroll.hasVerticalScroller = true
        scroll.hasHorizontalScroller = true
        scroll.documentView = table
        return scroll
    }

    func updateNSView(_ scroll: NSScrollView, context: Context) {
        guard let table = scroll.documentView as? NSTableView else { return }
        context.coordinator.accent = accent
        context.coordinator.mode = mode
        context.coordinator.update(table)
    }

    enum Column: String, CaseIterable {
        case file = "Filename"
        case gps = "GPS Coordinates"
        case road = "Road / Location"
        case city = "City"
        case state = "State / Region"
        case country = "Country"
        case altitude = "Altitude"
        case captureDate = "Capture Date"
        case cameraMake = "Camera Make"
        case cameraModel = "Camera Model"
        case lens = "Lens"
        case dimensions = "Dimensions"
        case fileSize = "File Size"
        case fileType = "File Type"
        case iso = "ISO"
        case aperture = "Aperture"
        case shutter = "Shutter Speed"
        case focalLength = "Focal Length"
        case tags = "Tags"
        case status = "Status"

        static let locationColumns: [Column] = [.gps, .road, .city, .state, .country, .altitude]
        static let metadataColumns: [Column] = [
            .captureDate, .cameraMake, .cameraModel, .lens, .dimensions, .fileSize, .fileType,
            .iso, .aperture, .shutter, .focalLength, .tags
        ]

        static func defaults(for mode: WorkspaceMode) -> [Column] {
            switch mode {
            case .metadata: return [.file, .tags, .cameraMake, .captureDate, .gps]
            case .map, .transfer: return [.file, .gps]
            }
        }

        var width: CGFloat {
            switch self {
            case .file: return 270
            case .gps: return 170
            case .road, .city, .state, .country: return 125
            case .altitude: return 86
            case .captureDate: return 155
            case .cameraMake, .cameraModel: return 130
            case .lens: return 160
            case .dimensions: return 100
            case .fileSize, .fileType, .aperture, .shutter, .focalLength: return 88
            case .iso, .tags: return 62
            case .status: return 78
            }
        }

        var minWidth: CGFloat {
            switch self {
            case .file: return 180
            case .iso, .tags: return 48
            default: return 65
            }
        }
    }

    final class Coordinator: NSObject, NSTableViewDataSource, NSTableViewDelegate, NSMenuDelegate {
        let library: Library
        var accent: MacAccent
        var mode: WorkspaceMode
        private var activeMode: WorkspaceMode?
        private var columns: [Column] = []
        private var updating = false
        private var columnsDirty = false
        private var lastItemsVersion = -1
        private var lastPendingVersion = -1
        private var lastSortKey = ""
        private var lastSortAscending = true
        private var lastAccentUsesBrand: Bool?
        private var rows: [MediaItem] = []
        private var snapshot: [String] = []
        private weak var currentTable: NSTableView?

        lazy var fileMenu: NSMenu = {
            let menu = NSMenu()
            menu.delegate = self
            menu.autoenablesItems = false
            return menu
        }()

        lazy var headerMenu: NSMenu = {
            let menu = NSMenu()
            menu.delegate = self
            menu.autoenablesItems = false
            return menu
        }()

        init(library: Library, accent: MacAccent, mode: WorkspaceMode) {
            self.library = library
            self.accent = accent
            self.mode = mode
        }

        private func defaultsKey(for mode: WorkspaceMode) -> String {
            "MediaColumns_\(mode.rawValue)"
        }

        private func savedColumns(for mode: WorkspaceMode) -> [Column] {
            let raw = JoongaTagStorage.stringArray(forKey: defaultsKey(for: mode)) ?? []
            var loaded = raw.compactMap(Column.init(rawValue:)).filter { $0 != .status }
            if loaded.isEmpty {
                loaded = Column.defaults(for: mode)
            } else if !loaded.contains(.file) {
                loaded.insert(.file, at: 0)
            }
            return loaded
        }

        private func saveColumns(_ columns: [Column], for mode: WorkspaceMode) {
            JoongaTagStorage.set(columns.map(\.rawValue), forKey: defaultsKey(for: mode))
        }

        func syncColumns(on table: NSTableView, mode: WorkspaceMode) {
            activeMode = mode
            let desired = savedColumns(for: mode)
            columns = desired
            columnsDirty = true
            let wasUpdating = updating
            updating = true

            let existingByID = Dictionary(uniqueKeysWithValues: table.tableColumns.map { ($0.identifier.rawValue, $0) })
            let desiredIDs = Set(desired.map(\.rawValue))
            for col in table.tableColumns where !desiredIDs.contains(col.identifier.rawValue) {
                table.removeTableColumn(col)
            }

            let singleColumn = desired.count <= 1
            table.columnAutoresizingStyle = singleColumn ? .firstColumnOnlyAutoresizingStyle : .noColumnAutoresizing

            for (targetIndex, field) in desired.enumerated() {
                if let col = existingByID[field.rawValue] {
                    col.resizingMask = singleColumn ? .autoresizingMask : [.userResizingMask, .autoresizingMask]
                    if let currentIndex = table.tableColumns.firstIndex(where: { $0 === col }), currentIndex != targetIndex {
                        table.moveColumn(currentIndex, toColumn: targetIndex)
                    }
                } else {
                    let col = NSTableColumn(identifier: .init(field.rawValue))
                    col.headerCell = TranslucentMediaHeaderCell(textCell: field.rawValue)
                    col.title = field.rawValue
                    col.minWidth = field.minWidth
                    col.width = field.width
                    col.resizingMask = singleColumn ? .autoresizingMask : [.userResizingMask, .autoresizingMask]
                    col.sortDescriptorPrototype = NSSortDescriptor(key: field.rawValue, ascending: true)
                    table.addTableColumn(col)
                    let addedIndex = table.tableColumns.count - 1
                    if addedIndex != targetIndex {
                        table.moveColumn(addedIndex, toColumn: targetIndex)
                    }
                }
            }
            if singleColumn {
                table.sizeToFit()
            }
            table.setAccessibilityLabel("Loaded files")
            updating = wasUpdating
        }

        func numberOfRows(in tableView: NSTableView) -> Int { rows.count }

        func update(_ table: NSTableView) {
            currentTable = table
            let modeChanged = activeMode != mode
            if modeChanged {
                syncColumns(on: table, mode: mode)
            }

            let sort = table.sortDescriptors.first
            let sortKey = sort?.key ?? Column.file.rawValue
            let sortAscending = sort?.ascending ?? true
            let itemsVersion = library.mediaItemsVersion
            let pendingVersion = library.pendingVersion
            let needsDataUpdate = modeChanged || columnsDirty || lastItemsVersion != itemsVersion ||
                lastPendingVersion != pendingVersion || lastSortKey != sortKey || lastSortAscending != sortAscending

            updating = true
            if needsDataUpdate {
                let field = Column(rawValue: sortKey) ?? .file
                rows = library.mediaItems.sorted { left, right in
                    let result = compare(left, right, by: field)
                    if result == .orderedSame { return left.id.localizedStandardCompare(right.id) == .orderedAscending }
                    return !sortAscending ? result == .orderedDescending : result == .orderedAscending
                }
                library.displayedMediaOrder = rows.map(\.id)
                let newSnapshot = rows.map { item in
                    let columnValues = columns.map { value(item, $0) }
                    return ([item.id, String(item.tags.count), library.hasPending(item.id) ? "1" : "0", mode.rawValue] + columnValues)
                        .joined(separator: "\u{0}")
                }
                if snapshot != newSnapshot {
                    snapshot = newSnapshot
                    table.reloadData()
                }
                lastItemsVersion = itemsVersion
                lastPendingVersion = pendingVersion
                lastSortKey = sortKey
                lastSortAscending = sortAscending
                columnsDirty = false
            }

            let indices = IndexSet(rows.indices.filter { library.mediaSelection.contains(rows[$0].id) })
            if indices != table.selectedRowIndexes {
                table.selectRowIndexes(indices, byExtendingSelection: false)
            }
            if lastAccentUsesBrand != accent.usesBrandColor {
                lastAccentUsesBrand = accent.usesBrandColor
                table.enumerateAvailableRowViews { row, _ in
                    if let row = row as? AccentMediaRow { row.accent = self.accent; row.refresh() }
                }
            }
            updating = false
        }

        func tableView(_ tableView: NSTableView, sortDescriptorsDidChange oldDescriptors: [NSSortDescriptor]) {
            update(tableView)
        }

        func tableViewColumnDidMove(_ notification: Notification) {
            guard !updating, let table = currentTable else { return }
            let reordered = table.tableColumns.compactMap { Column(rawValue: $0.identifier.rawValue) }
            guard !reordered.isEmpty else { return }
            columns = reordered
            saveColumns(reordered, for: mode)
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
            let isFile = field == .file
            let cellID = NSUserInterfaceItemIdentifier(isFile ? "MediaFileCell" : "MediaTextCell")
            let cell = (tableView.makeView(withIdentifier: cellID, owner: self) as? MediaTableCell)
                ?? MediaTableCell(isFileCell: isFile, identifier: cellID)
            cell.toolTip = item.id
            switch field {
            case .file:
                cell.configure(
                    value: item.name,
                    detail: item.folderPath,
                    thumbnail: item,
                    pending: library.hasPending(item.id)
                )
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
            if menu === headerMenu {
                buildHeaderMenu(menu)
                return
            }
            menu.removeAllItems()
            guard let table = currentTable else { return }
            if rows.indices.contains(table.clickedRow), !table.selectedRowIndexes.contains(table.clickedRow) {
                table.selectRowIndexes(IndexSet(integer: table.clickedRow), byExtendingSelection: false)
            }
            guard !library.selectedTargets.isEmpty else { return }
            for (title, action) in [
                ("Quick Look", #selector(previewSelection)),
                ("Reveal in Finder", #selector(revealSelection)),
                ("Stage Preset…", #selector(stageWorkflow)),
            ] {
                let item = menu.addItem(withTitle: title, action: action, keyEquivalent: "")
                item.target = self
                item.isEnabled = !library.busy
            }
            menu.addItem(.separator())
            let copyLoc = menu.addItem(withTitle: "Copy Location", action: #selector(copyLocation), keyEquivalent: "")
            copyLoc.target = self
            copyLoc.isEnabled = !library.busy && library.selectedMapLocation != nil
            let pasteLoc = menu.addItem(withTitle: "Paste Location", action: #selector(pasteLocation), keyEquivalent: "")
            pasteLoc.target = self
            pasteLoc.isEnabled = !library.busy && library.exif != nil && !library.selectedTargets.isEmpty
            menu.addItem(.separator())
            let removeCount = library.mediaSelection.count
            let removeTitle = removeCount > 1 ? "Remove \(removeCount) Files from List" : "Remove from List"
            let removeItem = menu.addItem(withTitle: removeTitle, action: #selector(removeSelection), keyEquivalent: "")
            removeItem.target = self
            removeItem.isEnabled = !library.busy && !library.mediaSelection.isEmpty
        }

        private func buildHeaderMenu(_ menu: NSMenu) {
            menu.removeAllItems()
            let visible = Set(columns)
            func addToggleItem(_ col: Column, to targetMenu: NSMenu) {
                let item = targetMenu.addItem(withTitle: col.rawValue, action: #selector(toggleColumn(_:)), keyEquivalent: "")
                item.target = self
                item.representedObject = col.rawValue
                item.state = visible.contains(col) ? .on : .off
                item.isEnabled = col != .file
            }

            if mode == .map {
                for col in Column.locationColumns { addToggleItem(col, to: menu) }
                menu.addItem(.separator())
                let moreItem = menu.addItem(withTitle: "More Metadata", action: nil, keyEquivalent: "")
                let moreMenu = NSMenu(title: "More Metadata")
                moreMenu.autoenablesItems = false
                for col in Column.metadataColumns { addToggleItem(col, to: moreMenu) }
                moreItem.submenu = moreMenu
            } else {
                for col in Column.metadataColumns { addToggleItem(col, to: menu) }
                menu.addItem(.separator())
                let locItem = menu.addItem(withTitle: "Location", action: nil, keyEquivalent: "")
                let locMenu = NSMenu(title: "Location")
                locMenu.autoenablesItems = false
                for col in Column.locationColumns { addToggleItem(col, to: locMenu) }
                locItem.submenu = locMenu
            }

            menu.addItem(.separator())
            let noneItem = menu.addItem(withTitle: "None (Filename Only)", action: #selector(clearOptionalColumns), keyEquivalent: "")
            noneItem.target = self
            noneItem.isEnabled = columns.count > 1
            let resetItem = menu.addItem(withTitle: "Reset Columns to Default", action: #selector(resetColumns), keyEquivalent: "")
            resetItem.target = self
        }

        @objc private func toggleColumn(_ item: NSMenuItem) {
            guard let raw = item.representedObject as? String, let col = Column(rawValue: raw),
                  col != .file, let table = currentTable else { return }
            var updated = columns
            if let idx = updated.firstIndex(of: col) {
                updated.remove(at: idx)
            } else {
                updated.append(col)
            }
            saveColumns(updated, for: mode)
            syncColumns(on: table, mode: mode)
            snapshot = []
            update(table)
        }

        @objc private func clearOptionalColumns() {
            guard let table = currentTable else { return }
            saveColumns([.file], for: mode)
            syncColumns(on: table, mode: mode)
            snapshot = []
            update(table)
        }

        @objc private func resetColumns() {
            guard let table = currentTable else { return }
            JoongaTagStorage.removeObject(forKey: defaultsKey(for: mode))
            syncColumns(on: table, mode: mode)
            snapshot = []
            update(table)
        }

        @objc private func previewSelection() { library.quickLook(library.selectedTargets) }
        @objc private func revealSelection() { NSWorkspace.shared.activateFileViewerSelecting(library.selectedTargets) }
        @objc private func stageWorkflow() { library.showWorkflows = true }
        @objc private func copyLocation() { library.copySelectedLocation() }
        @objc private func pasteLocation() {
            if let point = library.pastedLocation() {
                library.stageLocation(point, targets: library.selectedTargets)
            }
        }
        @objc private func removeSelection() { library.removeMedia(library.mediaSelection) }

        private func point(_ item: MediaItem) -> MapCoordinate? {
            library.locationDrafts[item.id] ?? item.pin.map { MapCoordinate($0.coordinate) }
        }

        private func value(_ item: MediaItem, _ field: Column) -> String {
            switch field {
            case .file: return item.name
            case .gps: return point(item)?.displayText ?? ""
            case .road: return item.road
            case .city: return item.city
            case .state: return item.state
            case .country: return item.country
            case .altitude: return item.altitude
            case .captureDate: return item.captureDate
            case .cameraMake: return item.make
            case .cameraModel: return item.model
            case .lens: return item.lens
            case .dimensions: return item.dimensions
            case .fileSize: return item.fileSize
            case .fileType: return item.fileType
            case .iso: return item.iso
            case .aperture: return item.aperture
            case .shutter: return item.shutter
            case .focalLength: return item.focalLength
            case .tags: return String(item.tagCount)
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

protocol AccentSelectionCell: AnyObject {
    func applySelection(_ foreground: NSColor?)
}

final class AccentMediaRow: NSTableRowView {
    var accent = MacAccent()

    override var isSelected: Bool {
        didSet {
            refresh()
            guard oldValue != isSelected, let table = superview as? NSTableView else { return }
            let row = table.row(for: self)
            guard row >= 0 else { return }
            for neighbour in [row - 1, row + 1] where (0..<table.numberOfRows).contains(neighbour) {
                table.rowView(atRow: neighbour, makeIfNecessary: false)?.needsDisplay = true
            }
        }
    }
    override var isEmphasized: Bool { didSet { refresh() } }
    override func viewDidMoveToWindow() { super.viewDidMoveToWindow(); refresh() }
    override func viewDidChangeEffectiveAppearance() { super.viewDidChangeEffectiveAppearance(); refresh() }
    override func didAddSubview(_ subview: NSView) { super.didAddSubview(subview); refresh() }

    private var usesEmphasis: Bool { isEmphasized || window?.isKeyWindow == true }

    func refresh() {
        let foreground = isSelected ? accent.selectionForeground(active: usesEmphasis) : nil
        for case let cell as AccentSelectionCell in subviews { cell.applySelection(foreground) }
        needsDisplay = true
    }

    override func drawSelection(in dirtyRect: NSRect) {
        guard isSelected else { return }
        accent.selectionBackground(active: usesEmphasis).setFill()
        var fill = bounds.insetBy(dx: 8, dy: 2)
        if let table = superview as? NSTableView {
            let row = table.row(for: self)
            if row > 0 && table.selectedRowIndexes.contains(row - 1) {
                if isFlipped { fill.origin.y -= 8 }
                fill.size.height += 8
            }
            if row >= 0 && table.selectedRowIndexes.contains(row + 1) {
                if !isFlipped { fill.origin.y -= 8 }
                fill.size.height += 8
            }
        }
        NSGraphicsContext.saveGraphicsState()
        NSBezierPath(rect: bounds).addClip()
        NSBezierPath(roundedRect: fill, xRadius: 6, yRadius: 6).fill()
        NSGraphicsContext.restoreGraphicsState()
    }
}

final class TranslucentMediaTable: NSTableView {
    override func drawBackground(inClipRect clipRect: NSRect) {
        let pitch = rowHeight + intercellSpacing.height
        guard pitch > 0 else { return }
        let origin = numberOfRows > 0 ? rect(ofRow: 0).minY : bounds.minY
        let first = max(0, Int(floor((clipRect.minY - origin) / pitch)))
        let end = max(first, Int(ceil((clipRect.maxY - origin) / pitch)))
        NSColor.black.withAlphaComponent(0.07).setFill()
        for row in first..<end where row.isMultiple(of: 2) {
            let stripe = NSRect(x: bounds.minX + 8, y: origin + CGFloat(row) * pitch,
                                width: max(0, bounds.width - 16), height: rowHeight)
            NSBezierPath(roundedRect: stripe, xRadius: 6, yRadius: 6).fill()
        }
    }
}

private final class TranslucentMediaHeaderView: NSTableHeaderView {
    override var isOpaque: Bool { false }
    override init(frame frameRect: NSRect) {
        super.init(frame: NSRect(x: 0, y: 0, width: frameRect.width, height: 30))
        wantsLayer = true
        updateTint()
    }

    private func updateTint() {
        effectiveAppearance.performAsCurrentDrawingAppearance {
            let dark = effectiveAppearance.bestMatch(from: [.darkAqua, .aqua]) == .darkAqua
            layer?.backgroundColor = NSColor(white: dark ? 0.18 : 0.91, alpha: 0.86).cgColor
        }
    }

    override func viewDidChangeEffectiveAppearance() {
        super.viewDidChangeEffectiveAppearance()
        updateTint()
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        updateTint()
        needsDisplay = true
    }

    override func layout() {
        super.layout()
        needsDisplay = true
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func draw(_ dirtyRect: NSRect) {
        if let table = tableView {
            for (index,column) in table.tableColumns.enumerated() {
                let rect = headerRect(ofColumn: index)
                guard rect.intersects(dirtyRect) else { continue }
                column.headerCell.draw(withFrame: rect, in: self)
                if let sort = table.sortDescriptors.first, sort.key == column.sortDescriptorPrototype?.key {
                    column.headerCell.drawSortIndicator(withFrame: rect, in: self, ascending: sort.ascending, priority: 0)
                }
            }
        }
        NSColor.separatorColor.withAlphaComponent(0.35).setFill()
        NSRect(x: bounds.minX, y: bounds.maxY - 1, width: bounds.width, height: 1).fill()
    }
}

private final class TranslucentMediaHeaderCell: NSTableHeaderCell {
    override func draw(withFrame cellFrame: NSRect, in controlView: NSView) {
        if isHighlighted {
            NSColor.black.withAlphaComponent(0.08).setFill()
            cellFrame.fill()
        }
        let paragraph = NSMutableParagraphStyle()
        paragraph.lineBreakMode = .byTruncatingTail
        let attrs: [NSAttributedString.Key: Any] = [
            .font: NSFont.systemFont(ofSize: 11, weight: .semibold),
            .foregroundColor: NSColor.secondaryLabelColor,
            .paragraphStyle: paragraph
        ]
        let title = NSAttributedString(string: stringValue, attributes: attrs)
        let size = title.size()
        let textRect = NSRect(
            x: cellFrame.minX + 12,
            y: cellFrame.minY + floor((cellFrame.height - size.height) / 2),
            width: max(0, cellFrame.width - 34),
            height: size.height
        )
        title.draw(in: textRect)
        if cellFrame.maxX < controlView.bounds.maxX - 1 {
            NSColor.separatorColor.withAlphaComponent(0.25).setFill()
            NSRect(x: cellFrame.maxX - 1, y: cellFrame.minY + 6, width: 1, height: max(0, cellFrame.height - 12)).fill()
        }
    }
}

private final class MediaThumbnailView: NSView {
    private let imageLayer = CALayer()
    private let placeholderView = NSImageView()
    private let anchorView = ThumbnailAnchorView()
    private var currentURL: URL?

    override init(frame frameRect: NSRect) {
        super.init(frame: frameRect)
        wantsLayer = true
        layer?.cornerRadius = 4
        layer?.masksToBounds = true
        layer?.backgroundColor = NSColor.labelColor.withAlphaComponent(0.06).cgColor

        imageLayer.frame = bounds
        imageLayer.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
        imageLayer.contentsGravity = .resizeAspectFill
        imageLayer.masksToBounds = true
        layer?.addSublayer(imageLayer)

        if let symbol = NSImage(systemSymbolName: "photo", accessibilityDescription: nil)?
            .withSymbolConfiguration(.init(pointSize: 13, weight: .regular)) {
            placeholderView.image = symbol
            placeholderView.contentTintColor = .tertiaryLabelColor
        }
        placeholderView.translatesAutoresizingMaskIntoConstraints = false
        addSubview(placeholderView)
        NSLayoutConstraint.activate([
            placeholderView.centerXAnchor.constraint(equalTo: centerXAnchor),
            placeholderView.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])

        anchorView.frame = bounds
        anchorView.autoresizingMask = [.width, .height]
        addSubview(anchorView)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func layout() {
        super.layout()
        imageLayer.frame = bounds
    }

    override func viewDidMoveToWindow() {
        super.viewDidMoveToWindow()
        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        imageLayer.contentsScale = scale
    }

    func configure(url: URL, itemID: String) {
        anchorView.row = QuickLookRow(id: itemID, originals: false)
        anchorView.url = url
        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        imageLayer.contentsScale = scale

        if currentURL == url, imageLayer.contents != nil {
            return
        }
        currentURL = url
        if let cached = ThumbnailStore.shared.cache.object(forKey: url as NSURL) {
            imageLayer.contents = cached
            anchorView.image = cached
            placeholderView.isHidden = true
            return
        }
        imageLayer.contents = nil
        anchorView.image = nil
        placeholderView.isHidden = false
        ThumbnailStore.shared.load(url) { [weak self] loaded in
            guard let self, self.currentURL == url else { return }
            self.imageLayer.contents = loaded
            self.anchorView.image = loaded
            self.placeholderView.isHidden = true
        }
    }
}

private final class MediaTableCell: NSTableCellView, AccentSelectionCell {
    private let titleLabel = NSTextField(labelWithString: "")
    private let detailLabel = NSTextField(labelWithString: "")
    private let thumbnailView = MediaThumbnailView(frame: NSRect(x: 0, y: 0, width: 42, height: 34))
    private let pendingIcon = NSImageView()
    private let isFileCell: Bool

    init(isFileCell: Bool, identifier: NSUserInterfaceItemIdentifier) {
        self.isFileCell = isFileCell
        super.init(frame: .zero)
        self.identifier = identifier

        titleLabel.font = isFileCell ? .systemFont(ofSize: 12.5, weight: .medium) : .systemFont(ofSize: 12)
        titleLabel.lineBreakMode = .byTruncatingMiddle
        titleLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)

        var labels: [NSView] = [titleLabel]
        if isFileCell {
            detailLabel.font = .systemFont(ofSize: 10.5)
            detailLabel.lineBreakMode = .byTruncatingMiddle
            detailLabel.setContentCompressionResistancePriority(.defaultLow, for: .horizontal)
            labels.append(detailLabel)
        }

        let textStack = NSStackView(views: labels)
        textStack.orientation = .vertical
        textStack.alignment = .leading
        textStack.spacing = 2

        if let symbol = NSImage(systemSymbolName: "circle.fill", accessibilityDescription: nil)?
            .withSymbolConfiguration(.init(pointSize: 6, weight: .regular)) {
            pendingIcon.image = symbol
        }
        pendingIcon.isHidden = true

        var views: [NSView] = []
        if isFileCell {
            thumbnailView.translatesAutoresizingMaskIntoConstraints = false
            thumbnailView.widthAnchor.constraint(equalToConstant: 42).isActive = true
            thumbnailView.heightAnchor.constraint(equalToConstant: 34).isActive = true
            views.append(thumbnailView)
        }
        views.append(textStack)
        views.append(pendingIcon)

        let contents = NSStackView(views: views)
        contents.spacing = 10
        contents.alignment = .centerY
        contents.translatesAutoresizingMaskIntoConstraints = false
        addSubview(contents)

        let horizontalInset: CGFloat = isFileCell ? 12 : 10
        NSLayoutConstraint.activate([
            contents.leadingAnchor.constraint(equalTo: leadingAnchor, constant: horizontalInset),
            contents.trailingAnchor.constraint(lessThanOrEqualTo: trailingAnchor, constant: -horizontalInset),
            contents.centerYAnchor.constraint(equalTo: centerYAnchor),
        ])
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    func configure(value: String, detail: String? = nil, thumbnail: MediaItem? = nil, pending: Bool = false) {
        titleLabel.stringValue = value
        if isFileCell {
            detailLabel.stringValue = detail ?? ""
            if let thumbnail {
                thumbnailView.configure(url: thumbnail.url, itemID: thumbnail.id)
            }
        }
        pendingIcon.isHidden = !pending
        setAccessibilityLabel([value, detail, pending ? "Pending changes" : nil].compactMap { $0 }.joined(separator: ", "))
        applySelection(nil)
    }

    func applySelection(_ foreground: NSColor?) {
        titleLabel.textColor = foreground ?? .labelColor
        if isFileCell {
            detailLabel.textColor = foreground ?? .secondaryLabelColor
        }
        pendingIcon.contentTintColor = foreground ?? .systemOrange
    }
}
