import AppKit
import SwiftUI
import MapKit

enum WorkspaceMode: String, CaseIterable, Identifiable {
    case preview = "Preview", metadata = "Metadata", map = "Map", transfer = "Transfer"
    var id: Self { self }
}

struct MediaItem: Identifiable {
    let url: URL
    let tags: [String: Any]
    let pin: LocationPin?
    var id: String { url.path }
    var name: String { url.lastPathComponent }
    var make: String { value("Make", "Keys:Make") }
    var captureDate: String { value("DateTimeOriginal", "QuickTime:CreateDate", "Keys:CreationDate", "CreateDate") }
    var tagCount: Int { tags.keys.filter { !$0.hasPrefix("System:") && !$0.hasPrefix("ExifTool:") && $0 != "SourceFile" }.count }
    func value(_ names: String...) -> String {
        for name in names {
            if let value = tags[name] { return String(describing: value) }
            if let key = tags.keys.sorted().first(where: { $0.hasSuffix(":" + name) }), let value = tags[key] {
                return String(describing: value)
            }
        }
        return ""
    }
}

extension Library {
    func chooseMedia() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = true
        panel.allowsMultipleSelection = true
        panel.prompt = "Add"
        if panel.runModal() == .OK { _ = addMedia(panel.urls) }
    }

    @discardableResult func addMedia(_ urls: [URL], selecting targetPaths: Set<String>? = nil) -> Bool {
        guard !busy, !mediaLoading, !urls.isEmpty else { return false }
        mediaLoading = true
        let existing = Set(mediaItems.map(\.url))
        DispatchQueue.global(qos: .userInitiated).async {
            let expanded = urls.flatMap { input -> [URL] in
                let url = input.standardizedFileURL.resolvingSymlinksInPath()
                guard let info = try? url.resourceValues(forKeys: [.isDirectoryKey, .isRegularFileKey]) else { return [] }
                if info.isDirectory == true { return self.list(url) }
                return info.isRegularFile == true && self.kind(url) != nil ? [url] : []
            }
            let items = Set(expanded).subtracting(existing).sorted { $0.path.localizedStandardCompare($1.path) == .orderedAscending }
                .map { MediaItem(url: $0, tags: self.read($0), pin: self.readLocation($0)) }
            DispatchQueue.main.async {
                self.mediaItems += items
                self.mediaLoading = false
                if let targetPaths {
                    self.mediaSelection = Set(self.mediaItems.map(\.id)).intersection(targetPaths)
                } else if self.mediaSelection.isEmpty, let first = items.first { self.mediaSelection = [first.id] }
                self.message = self.mediaItems.isEmpty ? "No supported media found" :
                    self.exif == nil ? "ExifTool is required to read and write metadata" : ""
            }
        }
        return true
    }

    func openMapForSelection() {
        guard !busy, !mediaLoading, !selectedTargets.isEmpty else { return }
        let targets = selectedTargets
        mode = .map
        addMedia(targets, selecting: Set(targets.map { $0.standardizedFileURL.resolvingSymlinksInPath().path }))
    }

    func refreshMedia(_ paths: [String]) {
        guard !paths.isEmpty else { return }
        let pending = mediaItems.filter { paths.contains($0.id) }.map(\.url)
        guard !pending.isEmpty else { return }
        mediaLoading = true
        DispatchQueue.global(qos: .userInitiated).async {
            let items = Dictionary(uniqueKeysWithValues: pending.map { url in
                (url.path, MediaItem(url: url, tags: self.read(url), pin: self.readLocation(url)))
            })
            DispatchQueue.main.async {
                self.mediaItems = self.mediaItems.map { items[$0.id] ?? $0 }
                self.mediaLoading = false
                self.inspect()
            }
        }
    }

    func location(for path: String) -> MapCoordinate? {
        if let draft = locationDrafts[path] { return draft }
        guard let coordinate = mediaItems.first(where: { $0.id == path })?.pin?.coordinate else { return nil }
        return MapCoordinate(coordinate)
    }

    func stageLocation(_ coordinate: MapCoordinate, targets: [URL]) {
        guard !busy, exif != nil, CLLocationCoordinate2DIsValid(coordinate.coordinate), !targets.isEmpty else { return }
        for target in targets { locationDrafts[target.path] = coordinate }
        message = "Location staged for \(targets.count) \(targets.count == 1 ? "file" : "files")"
    }

    var selectedMapLocation: MapCoordinate? {
        guard mediaSelection.count == 1, let path = mediaSelection.first else { return nil }
        return location(for: path)
    }

    func copySelectedLocation() {
        guard let point = selectedMapLocation else { return }
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(point.text, forType: .string)
    }

    func pastedLocation() -> MapCoordinate? {
        guard let text = NSPasteboard.general.string(forType: .string), let point = MapCoordinate(text) else {
            message = "Clipboard has no valid latitude and longitude"
            return nil
        }
        return point
    }
}

struct MapCoordinate: Codable, Hashable {
    let latitude: Double
    let longitude: Double
    init(_ coordinate: CLLocationCoordinate2D) { latitude = coordinate.latitude; longitude = coordinate.longitude }
    var coordinate: CLLocationCoordinate2D { .init(latitude: latitude, longitude: longitude) }
    var text: String { String(format: "%.7f, %.7f", locale: Locale(identifier: "en_US_POSIX"), latitude, longitude) }

    init?(_ text: String) {
        let fields = text.split(separator: ",")
        guard fields.count == 2,
              let lat = Double(fields[0].trimmingCharacters(in: .whitespacesAndNewlines)),
              let lon = Double(fields[1].trimmingCharacters(in: .whitespacesAndNewlines)),
              lat.isFinite, lon.isFinite, (-90...90).contains(lat), (-180...180).contains(lon) else { return nil }
        self.init(.init(latitude: lat, longitude: lon))
    }
}

enum WorkspaceMapStyle: String, CaseIterable, Identifiable {
    case standard = "Normal", hybrid = "Hybrid", satellite = "Satellite"
    var id: Self { self }
    var type: MKMapType {
        switch self {
        case .standard: return .standard
        case .hybrid: return .hybrid
        case .satellite: return .satellite
        }
    }
}

struct MediaWorkspace: View {
    @ObservedObject var library: Library
    var body: some View {
        Group {
            if library.mediaItems.isEmpty && library.mode != .map {
                emptyFiles
            } else if library.mode == .preview {
                MediaTable(library: library, compact: false)
            } else {
                HSplitView {
                    Group {
                        if library.mediaItems.isEmpty { emptyFiles }
                        else { MediaTable(library: library, compact: true) }
                    }.frame(minWidth: 230, idealWidth: 315)
                    if library.mode == .map {
                        WorkspaceMap(library: library).frame(minWidth: 490, maxWidth: .infinity)
                    } else {
                        Inspector(library: library).frame(minWidth: 480, maxWidth: .infinity)
                    }
                }
            }
        }
        .disabled(library.busy)
        .dropDestination(for: URL.self) { urls,_ in library.addMedia(urls) }
    }

    private var emptyFiles: some View {
        VStack(spacing: 12) {
            Image(systemName: "photo.on.rectangle.angled").font(.system(size: 32, weight: .light))
                .foregroundStyle(.secondary)
            Text(library.mediaLoading ? "Reading metadata…" : "Drop photos, videos, or folders here")
                .font(.headline).multilineTextAlignment(.center)
            Button("Add Files or Folders…", action: library.chooseMedia).disabled(library.mediaLoading)
        }.padding(16).frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}

private struct FavouriteLocation: Codable, Identifiable {
    var id = UUID()
    let name: String
    let point: MapCoordinate
}

private struct WorkspaceMap: View {
    @ObservedObject var library: Library
    @AppStorage("MapFavourites") private var favouritesData = Data()
    @AppStorage("WorkspaceMapStyle") private var mapStyle = WorkspaceMapStyle.standard
    @AppStorage("MapShowsSelectedOnly") private var selectedOnly = false
    @State private var focus: LocationPin?
    @State private var fitRequest: UUID?
    @State private var lastPoint: MapCoordinate?
    @State private var query = ""
    @State private var search: MKLocalSearch?
    @State private var results: [LocationPin] = []
    @State private var status = ""
    @State private var naming = false
    @State private var name = ""

    private var favourites: [FavouriteLocation] { (try? JSONDecoder().decode([FavouriteLocation].self, from: favouritesData)) ?? [] }
    private var pins: [LocationPin] {
        library.mediaItems.compactMap { item in
            guard (!selectedOnly || library.mediaSelection.contains(item.id)), let point = library.locationDrafts[item.id] ?? item.pin.map({ MapCoordinate($0.coordinate) }) else { return nil }
            return LocationPin(id: item.id, title: item.name, coordinate: point.coordinate, staged: library.locationDrafts[item.id] != nil)
        }
    }
    private var sourcePoint: MapCoordinate? {
        library.selectedMapLocation
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack {
                TextField("Search for a place or address", text: $query).textFieldStyle(.roundedBorder).onSubmit(find)
                    .onChange(of: query) { _ in
                        results = []
                        if search == nil { status = "" }
                    }
                Button("Search", action: find).disabled(query.trimmingCharacters(in: .whitespaces).isEmpty || search != nil)
                Menu {
                    Picker("Map Style", selection: $mapStyle) { ForEach(WorkspaceMapStyle.allCases) { Text($0.rawValue).tag($0) } }
                    Toggle("Show Selected Files Only", isOn: $selectedOnly)
                    Button("Fit Visible Pins") { fitRequest = UUID() }.disabled(pins.isEmpty)
                } label: { Label("Map Options", systemImage: "map") }.fixedSize()
            }.tint(.primary).padding(10)
            if !results.isEmpty {
                ScrollView {
                    VStack(alignment: .leading) {
                        ForEach(results) { place in
                            Button(place.title) { stage(MapCoordinate(place.coordinate)); results = [] }
                                .buttonStyle(.borderless).tint(.primary)
                        }
                    }.frame(maxWidth: .infinity, alignment: .leading).padding(10)
                }.frame(maxHeight: 110)
            }
            if !status.isEmpty { Text(status).font(.caption).foregroundStyle(.secondary).padding(.bottom, 6) }
            Divider()
            WorkspaceMapView(pins: pins, searchSelection: focus, style: mapStyle, fitRequest: fitRequest, onPinSelect: { id in
                library.mediaSelection = [id]
            }) { coordinate in
                lastPoint = MapCoordinate(coordinate)
                library.stageLocation(MapCoordinate(coordinate), targets: library.selectedTargets)
            }
            Divider()
            HStack(spacing: 8) {
                Button("Copy Location", action: library.copySelectedLocation)
                    .disabled(sourcePoint == nil).help("Copy location · ⇧⌘C")
                Button("Paste Location") {
                    if let point = library.pastedLocation() { stage(point) }
                }.disabled(library.mediaSelection.isEmpty || library.exif == nil).help("Paste location · ⇧⌘V")
                Menu("Favourites") {
                    Button("Save Current Location…") { name = ""; naming = true }.disabled(sourcePoint == nil && lastPoint == nil)
                    ForEach(favourites) { favourite in
                        Button(favourite.name) { focusPoint(favourite.point) }
                    }
                    if !favourites.isEmpty {
                        Divider()
                        Menu("Apply to Selection") {
                            ForEach(favourites) { favourite in Button(favourite.name) { stage(favourite.point) } }
                        }.disabled(library.mediaSelection.isEmpty || library.exif == nil)
                        Menu("Remove Favourite") {
                            ForEach(favourites) { favourite in
                                Button(favourite.name) { store(favourites.filter { $0.id != favourite.id }) }
                            }
                        }
                    }
                }.fixedSize()
                Spacer(minLength: 4)
                Text(library.mediaSelection.isEmpty ? "Select a file to set its location" : "\(pins.count) on map")
                    .font(.caption).foregroundStyle(.secondary).lineLimit(1)
            }.tint(.primary).padding(10)
        }
        .onChange(of: library.mediaSelection) { ids in if ids.count == 1, let sourcePoint { focusPoint(sourcePoint) } }
        .onDisappear { search?.cancel() }
        .alert("Save Favourite Location", isPresented: $naming) {
            TextField("Name, e.g. Home", text: $name)
            Button("Cancel", role: .cancel) {}
            Button("Save") {
                let value = name.trimmingCharacters(in: .whitespacesAndNewlines)
                if !value.isEmpty, let point = lastPoint ?? sourcePoint {
                    store(favourites.filter { $0.name.caseInsensitiveCompare(value) != .orderedSame } + [.init(name: value, point: point)])
                }
            }
        }
    }

    private func store(_ items: [FavouriteLocation]) { if let data = try? JSONEncoder().encode(items) { favouritesData = data } }
    private func focusPoint(_ point: MapCoordinate) {
        lastPoint = point
        focus = LocationPin(id: UUID().uuidString, title: "", coordinate: point.coordinate)
    }
    private func stage(_ point: MapCoordinate) {
        library.stageLocation(point, targets: library.selectedTargets)
        focusPoint(point)
    }
    private func find() {
        let value = query.trimmingCharacters(in: .whitespacesAndNewlines)
        guard search == nil, !value.isEmpty else { return }
        let request = MKLocalSearch.Request()
        request.naturalLanguageQuery = value
        let operation = MKLocalSearch(request: request)
        search = operation
        results = []
        status = "Searching…"
        Task { @MainActor in
            defer {
                search = nil
                if value != query.trimmingCharacters(in: .whitespacesAndNewlines) { status = "" }
            }
            do {
                let response = try await operation.start()
                guard value == query.trimmingCharacters(in: .whitespacesAndNewlines) else { return }
                results = response.mapItems.prefix(6).map {
                    .init(id: UUID().uuidString, title: $0.placemark.title ?? $0.name ?? value, coordinate: $0.placemark.coordinate)
                }
                status = results.isEmpty ? "No places found." : ""
            } catch { status = "No places found or search is unavailable." }
        }
    }
}
