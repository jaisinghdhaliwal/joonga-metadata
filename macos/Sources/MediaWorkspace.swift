import AppKit
import SwiftUI
import MapKit

enum WorkspaceMode: String, CaseIterable, Identifiable {
    case metadata = "Metadata", map = "Map", transfer = "Transfer"
    var id: Self { self }
}

struct MediaItem: Identifiable {
    let url: URL
    let tags: [String: Any]
    let pin: LocationPin?
    let id: String
    let name: String
    let folderPath: String
    let make: String
    let model: String
    let lens: String
    let captureDate: String
    let dimensions: String
    let fileSize: String
    let fileType: String
    let iso: String
    let aperture: String
    let shutter: String
    let focalLength: String
    let road: String
    let city: String
    let state: String
    let country: String
    let altitude: String
    let tagCount: Int

    init(url: URL, tags: [String: Any], pin: LocationPin?) {
        self.url = url
        self.tags = tags
        self.pin = pin
        self.id = url.path
        self.name = url.lastPathComponent
        self.folderPath = (url.deletingLastPathComponent().path as NSString).abbreviatingWithTildeInPath

        var suffixMap = [String: Any]()
        var count = 0
        if !tags.isEmpty {
            suffixMap.reserveCapacity(tags.count)
            for key in tags.keys.sorted() {
                if !key.hasPrefix("System:") && !key.hasPrefix("ExifTool:") && key != "SourceFile" {
                    count += 1
                }
                if let colon = key.firstIndex(of: ":") {
                    let short = String(key[key.index(after: colon)...])
                    if suffixMap[short] == nil {
                        suffixMap[short] = tags[key]
                    }
                }
            }
        }
        func lookup(_ names: String...) -> String {
            for name in names {
                if let val = tags[name] { return String(describing: val) }
                if let val = suffixMap[name] { return String(describing: val) }
            }
            return ""
        }

        self.make = lookup("Make", "Keys:Make")
        self.model = lookup("Model", "CameraModelName", "Keys:Model")
        self.lens = lookup("LensModel", "LensID", "Lens")
        self.captureDate = lookup("DateTimeOriginal", "QuickTime:CreateDate", "Keys:CreationDate", "CreateDate")
        self.dimensions = lookup("ImageSize")
        self.fileSize = lookup("FileSize")
        self.fileType = lookup("FileType")
        self.iso = lookup("ISO")
        self.aperture = lookup("FNumber", "Aperture")
        self.shutter = lookup("ExposureTime", "ShutterSpeed")
        self.focalLength = lookup("FocalLength")
        self.road = lookup("Sub-location", "Location")
        self.city = lookup("City")
        self.state = lookup("State", "Province-State")
        self.country = lookup("Country", "Country-PrimaryLocationName")
        self.altitude = lookup("GPSAltitude")
        self.tagCount = count
    }

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

    func locationPin(from tags: [String: Any], url: URL) -> LocationPin? {
        func parseNumber(_ raw: Any?) -> Double? {
            if let num = raw as? NSNumber { return num.doubleValue }
            guard let str = raw as? String else { return nil }
            let cleaned = str.trimmingCharacters(in: .whitespacesAndNewlines)
                .replacingOccurrences(of: "°", with: "")
                .replacingOccurrences(of: "+", with: "")
            return Double(cleaned)
        }
        func applyRef(_ value: Double, refRaw: Any?, negativeTokens: Set<String>) -> Double {
            guard value > 0, let ref = (refRaw as? String)?.trimmingCharacters(in: .whitespacesAndNewlines).lowercased(),
                  negativeTokens.contains(ref) else { return value }
            return -value
        }

        var latitude: Double?
        var longitude: Double?

        if let lat = parseNumber(tags["Composite:GPSLatitude"]),
           let lon = parseNumber(tags["Composite:GPSLongitude"]) {
            latitude = lat
            longitude = lon
        } else if let lat = parseNumber(tags["GPS:GPSLatitude"] ?? tags["EXIF:GPSLatitude"]),
                  let lon = parseNumber(tags["GPS:GPSLongitude"] ?? tags["EXIF:GPSLongitude"]) {
            latitude = applyRef(lat, refRaw: tags["GPS:GPSLatitudeRef"] ?? tags["EXIF:GPSLatitudeRef"], negativeTokens: ["s", "south"])
            longitude = applyRef(lon, refRaw: tags["GPS:GPSLongitudeRef"] ?? tags["EXIF:GPSLongitudeRef"], negativeTokens: ["w", "west"])
        } else {
            let combined = (tags["Keys:GPSCoordinates"] as? String)
                ?? (tags["QuickTime:GPSCoordinates"] as? String)
                ?? (tags["Composite:GPSPosition"] as? String)
            if let combined {
                let tokens = combined
                    .replacingOccurrences(of: ",", with: " ")
                    .split(whereSeparator: \.isWhitespace)
                    .map(String.init)
                if tokens.count >= 2, let lat = parseNumber(tokens[0]), let lon = parseNumber(tokens[1]) {
                    latitude = lat
                    longitude = lon
                }
            }
        }

        guard let lat = latitude, let lon = longitude,
              lat.isFinite, lon.isFinite,
              (-90...90).contains(lat), (-180...180).contains(lon) else { return nil }
        return LocationPin(id: url.path, title: url.lastPathComponent,
                           coordinate: CLLocationCoordinate2D(latitude: lat, longitude: lon))
    }

    @discardableResult func addMedia(_ urls: [URL], selecting targetPaths: Set<String>? = nil) -> Bool {
        guard !busy, !mediaLoading, !urls.isEmpty else { return false }
        mediaLoading = true
        mediaLoadVersion += 1
        let version = mediaLoadVersion
        let existing = Set(mediaItems.map(\.url))
        DispatchQueue.global(qos: .userInitiated).async {
            let expanded = urls.flatMap { input -> [URL] in
                let url = input.standardizedFileURL.resolvingSymlinksInPath()
                guard let info = try? url.resourceValues(forKeys: [.isDirectoryKey, .isRegularFileKey]) else { return [] }
                if info.isDirectory == true {
                    return self.list(url).map { $0.standardizedFileURL.resolvingSymlinksInPath() }
                }
                return info.isRegularFile == true && self.kind(url) != nil ? [url] : []
            }
            let newURLs = Set(expanded).subtracting(existing).sorted {
                $0.path.localizedStandardCompare($1.path) == .orderedAscending
            }
            let placeholders = newURLs.map { MediaItem(url: $0, tags: [:], pin: nil) }
            DispatchQueue.main.async {
                guard self.mediaLoadVersion == version else { return }
                self.mediaItems += placeholders
                if let targetPaths {
                    self.mediaSelection = Set(self.mediaItems.map(\.id)).intersection(targetPaths)
                } else if self.mediaSelection.isEmpty, let first = placeholders.first {
                    self.mediaSelection = [first.id]
                }
                if self.mediaItems.isEmpty {
                    self.mediaLoading = false
                    self.loadingProgress = nil
                    self.message = "No supported media found"
                } else if self.exif == nil {
                    self.loadingProgress = newURLs.isEmpty ? nil : 0.04
                    self.message = "ExifTool is required to read and write metadata"
                } else {
                    self.loadingProgress = newURLs.isEmpty ? nil : 0.04
                    self.message = newURLs.isEmpty ? "" : "Reading metadata…"
                }
            }

            guard !newURLs.isEmpty else { return }
            let chunkSize = 60
            for start in stride(from: 0, to: newURLs.count, by: chunkSize) {
                let processedCount = min(start + chunkSize, newURLs.count)
                let chunk = Array(newURLs[start..<processedCount])
                let tagMap = self.readBatch(chunk)
                let resolved = chunk.map { url -> MediaItem in
                    let normalizedPath = url.standardizedFileURL.resolvingSymlinksInPath().path
                    let tags = tagMap[url.path] ?? tagMap[normalizedPath] ?? [:]
                    return MediaItem(url: url, tags: tags, pin: self.locationPin(from: tags, url: url))
                }
                let updatedIDs = Set(resolved.map(\.id))
                let isLast = processedCount >= newURLs.count
                let progress = Double(processedCount) / Double(newURLs.count)
                DispatchQueue.main.async {
                    guard self.mediaLoadVersion == version else { return }
                    let byID = Dictionary(uniqueKeysWithValues: resolved.map { ($0.id, $0) })
                    self.mediaItems = self.mediaItems.map { byID[$0.id] ?? $0 }
                    if !self.mediaSelection.isDisjoint(with: updatedIDs) {
                        self.inspect()
                    }
                    if isLast {
                        self.loadingProgress = nil
                        self.mediaLoading = false
                        self.message = self.exif == nil ? "ExifTool is required to read and write metadata" : ""
                    } else {
                        self.loadingProgress = progress
                    }
                }
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
        loadingProgress = 0.04
        mediaLoadVersion += 1
        let version = mediaLoadVersion
        DispatchQueue.global(qos: .userInitiated).async {
            let chunkSize = 60
            for start in stride(from: 0, to: pending.count, by: chunkSize) {
                let processedCount = min(start + chunkSize, pending.count)
                let chunk = Array(pending[start..<processedCount])
                let tagMap = self.readBatch(chunk)
                let resolved = Dictionary(uniqueKeysWithValues: chunk.map { url in
                    let normalizedPath = url.standardizedFileURL.resolvingSymlinksInPath().path
                    let tags = tagMap[url.path] ?? tagMap[normalizedPath] ?? [:]
                    return (url.path, MediaItem(url: url, tags: tags, pin: self.locationPin(from: tags, url: url)))
                })
                let isLast = processedCount >= pending.count
                let progress = Double(processedCount) / Double(pending.count)
                DispatchQueue.main.async {
                    guard self.mediaLoadVersion == version else { return }
                    self.mediaItems = self.mediaItems.map { resolved[$0.id] ?? $0 }
                    if isLast {
                        self.loadingProgress = nil
                        self.mediaLoading = false
                        self.inspect()
                    } else {
                        self.loadingProgress = progress
                    }
                }
            }
        }
    }

    func removeMedia(_ ids: Set<String>) {
        guard !busy, !ids.isEmpty else { return }
        mediaItems.removeAll { ids.contains($0.id) }
        mediaSelection.subtract(ids)
        for id in ids {
            drafts[id] = nil
            locationDrafts[id] = nil
            workflowDrafts[id] = nil
        }
        inspect()
    }

    func clearMedia() {
        guard !busy else { return }
        if !pendingPaths.isEmpty {
            let alert = NSAlert()
            alert.messageText = "Discard unsaved metadata edits?"
            alert.informativeText = "Pending edits will be lost if you clear the loaded files."
            alert.addButton(withTitle: "Keep Editing")
            alert.addButton(withTitle: "Clear Files").hasDestructiveAction = true
            guard alert.runModal() == .alertSecondButtonReturn else { return }
        }
        mediaLoadVersion += 1
        mediaLoading = false
        loadingProgress = nil
        if mode == .transfer {
            source = nil
            destination = nil
            sourceItems = []
            exportItems = []
            files = []
            unmatchedOriginals = []
            originalSelection = []
            exportSelection = []
            selection = nil
        } else {
            mediaItems = []
            mediaSelection = []
        }
        drafts = [:]
        locationDrafts = [:]
        workflowDrafts = [:]
        metadata = []
        locationPins = []
        message = ""
    }

    func location(for path: String) -> MapCoordinate? {
        if let draft = locationDrafts[path] { return draft }
        if let coordinate = mediaItems.first(where: { $0.id == path })?.pin?.coordinate {
            return MapCoordinate(coordinate)
        }
        if let coordinate = locationPins.first(where: { $0.id == path })?.coordinate {
            return MapCoordinate(coordinate)
        }
        return nil
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

    var selectedLocation: MapCoordinate? {
        if mode != .transfer { return selectedMapLocation }
        guard selectedTargets.count == 1, let url = selectedTargets.first else { return nil }
        return location(for: url.path)
    }

    func copySelectedLocation() {
        guard let point = selectedLocation else { return }
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

    var favouriteLocations: [FavouriteLocation] {
        _ = favouriteLocationsVersion
        guard let data = UserDefaults.standard.data(forKey: "MapFavourites") else { return [] }
        return (try? JSONDecoder().decode([FavouriteLocation].self, from: data)) ?? []
    }

    func storeFavouriteLocations(_ items: [FavouriteLocation]) {
        if let data = try? JSONEncoder().encode(items) {
            UserDefaults.standard.set(data, forKey: "MapFavourites")
            favouriteLocationsVersion = UUID()
        }
    }

    func saveFavouriteLocation(name: String, point: MapCoordinate) {
        let value = name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return }
        let updated = favouriteLocations.filter { $0.name.caseInsensitiveCompare(value) != .orderedSame } + [.init(name: value, point: point)]
        storeFavouriteLocations(updated)
    }

    func removeFavouriteLocation(id: UUID) {
        storeFavouriteLocations(favouriteLocations.filter { $0.id != id })
    }

    func focusMapPoint(_ point: MapCoordinate) {
        lastMapPoint = point
        mapFocusRequest = LocationPin(id: UUID().uuidString, title: "", coordinate: point.coordinate)
    }
}

struct MapCoordinate: Codable, Hashable {
    let latitude: Double
    let longitude: Double
    init(_ coordinate: CLLocationCoordinate2D) { latitude = coordinate.latitude; longitude = coordinate.longitude }
    var coordinate: CLLocationCoordinate2D { .init(latitude: latitude, longitude: longitude) }
    var text: String { String(format: "%.7f, %.7f", locale: Locale(identifier: "en_US_POSIX"), latitude, longitude) }
    var displayText: String { String(format: "%.5f°, %.5f°", locale: Locale(identifier: "en_US_POSIX"), latitude, longitude) }

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
    @State private var mapMounted = false

    var body: some View {
        HSplitView {
            Group {
                if library.mediaItems.isEmpty { emptyFiles }
                else { MediaTable(library: library, mode: library.mode) }
            }
            .frame(minWidth: 240, maxWidth: .infinity)
            .layoutPriority(1)
            .background(Vibrancy(material: .underWindowBackground, blendingMode: .behindWindow))

            if library.mode == .map || library.showInspector {
                ZStack {
                    if mapMounted || library.mode == .map {
                        WorkspaceMap(library: library, isActive: library.mode == .map)
                            .opacity(library.mode == .map ? 1 : 0)
                            .allowsHitTesting(library.mode == .map)
                            .accessibilityHidden(library.mode != .map)
                    }
                    if library.mode == .metadata && library.showInspector {
                        Inspector(library: library)
                    }
                }
                .frame(minWidth: library.mode == .map ? 460 : 360, idealWidth: library.mode == .map ? 460 : 360, maxWidth: .infinity)
            }
        }
        .onAppear {
            if library.mode == .map { mapMounted = true }
        }
        .onChange(of: library.mode) { mode in
            if mode == .map { mapMounted = true }
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
        }
        .padding(.vertical, 22)
        .padding(.horizontal, 26)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background {
            Canvas { context, size in
                for row in stride(from: 0, to: Int(ceil(size.height / 46)), by: 2) {
                    let stripe = CGRect(x: 8, y: CGFloat(row) * 46, width: max(0, size.width - 16), height: 46)
                    context.fill(Path(roundedRect: stripe, cornerRadius: 6), with: .color(.black.opacity(0.07)))
                }
            }
            .allowsHitTesting(false)
            .accessibilityHidden(true)
        }
    }
}

struct FavouriteLocation: Codable, Identifiable {
    var id = UUID()
    let name: String
    let point: MapCoordinate
}

private extension View {
    @ViewBuilder
    func mapControlSurface<S: Shape>(in shape: S, interactive: Bool = false) -> some View {
        if #available(macOS 26, *) {
            glassEffect(interactive ? .regular.interactive() : .regular, in: shape)
        } else {
            background(.regularMaterial, in: shape)
                .shadow(color: .black.opacity(0.12), radius: 8, y: 3)
        }
    }

    @ViewBuilder
    func mapTintedGlassSurface<S: Shape>(_ tint: Color, in shape: S) -> some View {
        if #available(macOS 26, *) {
            glassEffect(.regular.tint(tint).interactive(), in: shape)
        } else {
            background(tint, in: shape)
                .shadow(color: .black.opacity(0.14), radius: 8, y: 3)
        }
    }
}

private struct MapGlassGroup<Content: View>: View {
    @ViewBuilder let content: Content

    var body: some View {
        if #available(macOS 26, *) {
            GlassEffectContainer(spacing: 10) {
                HStack(spacing: 10) {
                    content
                }
            }
        } else {
            HStack(spacing: 10) {
                content
            }
        }
    }
}

private struct WorkspaceMap: View {
    @ObservedObject var library: Library
    let isActive: Bool
    @Environment(\.joongaAccent) private var accent
    @AppStorage("WorkspaceMapStyle") private var mapStyle = WorkspaceMapStyle.standard
    @AppStorage("MapShowsSelectedOnly") private var selectedOnly = false
    @State private var fitRequest: UUID?
    @State private var query = ""
    @State private var search: MKLocalSearch?
    @State private var results: [LocationPin] = []
    @State private var status = ""
    @State private var name = ""

    private var pins: [LocationPin] {
        guard isActive else { return [] }
        return library.mediaItems.compactMap { item in
            guard (!selectedOnly || library.mediaSelection.contains(item.id)), let point = library.locationDrafts[item.id] ?? item.pin.map({ MapCoordinate($0.coordinate) }) else { return nil }
            return LocationPin(id: item.id, title: item.name, coordinate: point.coordinate, staged: library.locationDrafts[item.id] != nil)
        }
    }
    private var sourcePoint: MapCoordinate? {
        library.selectedMapLocation
    }

    var body: some View {
        WorkspaceMapView(pins: pins, searchSelection: library.mapFocusRequest, style: mapStyle, fitRequest: fitRequest, isActive: isActive, accent: accent, onSelect: { ids in
            library.mediaSelection = ids
        }) { coordinate in
            let point = MapCoordinate(coordinate)
            library.lastMapPoint = point
            library.stageLocation(point, targets: library.selectedTargets)
        }
        .overlay(alignment: .topLeading) {
            VStack(alignment: .leading, spacing: 10) {
                MapGlassGroup {
                    HStack(spacing: 8) {
                        Image(systemName: "magnifyingglass")
                            .foregroundStyle(.secondary)
                        TextField("Search for a place or address", text: $query)
                            .textFieldStyle(.plain)
                            .onSubmit(find)
                            .onChange(of: query) { _ in
                                results = []
                                if search == nil { status = "" }
                            }
                        if !query.isEmpty {
                            Button {
                                query = ""
                                results = []
                                if search == nil { status = "" }
                            } label: {
                                Image(systemName: "xmark.circle.fill")
                                    .foregroundStyle(.secondary)
                            }
                            .buttonStyle(.plain)
                            .help("Clear search")
                        }
                    }
                    .padding(.horizontal, 14)
                    .frame(height: 36)
                    .frame(minWidth: 90, maxWidth: 320)
                    .mapControlSurface(in: Capsule(), interactive: true)

                    Button(action: find) {
                        Image(systemName: "magnifyingglass").accessibilityLabel("Search")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundStyle(accent.selectionText(active: true))
                            .padding(.horizontal, 16)
                            .frame(height: 36)
                    }
                    .buttonStyle(.plain)
                    .mapTintedGlassSurface(accent.tint, in: Capsule())
                    .opacity(query.trimmingCharacters(in: .whitespaces).isEmpty || search != nil ? 0.5 : 1)
                    .disabled(query.trimmingCharacters(in: .whitespaces).isEmpty || search != nil)

                    Menu {
                        Picker("Map Style", selection: $mapStyle) { ForEach(WorkspaceMapStyle.allCases) { Text($0.rawValue).tag($0) } }
                        Toggle("Show Selected Files Only", isOn: $selectedOnly)
                        Button("Fit Visible Pins") { fitRequest = UUID() }.disabled(pins.isEmpty)
                    } label: {
                        Label("Map Options", systemImage: "map").labelStyle(.iconOnly)
                            .font(.system(size: 13, weight: .medium))
                    }
                    .menuStyle(.borderlessButton)
                    .fixedSize()
                    .tint(.primary)
                    .padding(.horizontal, 14)
                    .frame(height: 36)
                    .mapControlSurface(in: Capsule(), interactive: true)
                }

                if !results.isEmpty {
                    ScrollView {
                        VStack(alignment: .leading, spacing: 6) {
                            ForEach(results) { place in
                                Button(place.title) { stage(MapCoordinate(place.coordinate)); results = [] }
                                    .buttonStyle(.borderless).tint(.primary)
                                    .frame(maxWidth: .infinity, alignment: .leading)
                            }
                        }
                        .padding(12)
                    }
                    .frame(maxWidth: 320, maxHeight: 150)
                    .mapControlSurface(in: RoundedRectangle(cornerRadius: 14, style: .continuous), interactive: true)
                }

                if !status.isEmpty {
                    Text(status)
                        .font(.caption)
                        .foregroundStyle(.secondary)
                        .padding(.horizontal, 12)
                        .padding(.vertical, 6)
                        .mapControlSurface(in: Capsule())
                }
            }
            .padding(.top, 14)
            .padding(.leading, 14)
            .padding(.trailing, 72)
        }
        .onChange(of: library.mediaSelection) { ids in
            guard isActive else { return }
            if ids.count == 1, let sourcePoint { focusPoint(sourcePoint) }
        }
        .onDisappear { search?.cancel() }
        .alert("Save Favourite Location", isPresented: $library.requestSaveFavourite) {
            TextField("Name, e.g. Home", text: $name)
            Button("Cancel", role: .cancel) { name = "" }
            Button("Save") {
                if let point = library.lastMapPoint ?? sourcePoint {
                    library.saveFavouriteLocation(name: name, point: point)
                }
                name = ""
            }
        }
    }

    private func focusPoint(_ point: MapCoordinate) {
        library.focusMapPoint(point)
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
