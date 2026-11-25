import SwiftUI
import MapKit

struct LocationPin: Identifiable {
    let id: String
    let title: String
    let coordinate: CLLocationCoordinate2D
    var staged = false

    var fileURL: URL? { id.hasPrefix("/") ? URL(fileURLWithPath: id) : nil }
}

private struct PhotoMapPin: View {
    let url: URL?
    let staged: Bool
    let size: CGFloat

    var body: some View {
        let shape = RoundedRectangle(cornerRadius: 6, style: .continuous)
        Thumbnail(url: url, contentMode: .fill)
            .frame(width: size - 6, height: size - 6)
            .background(Color(nsColor: .controlBackgroundColor))
            .clipShape(RoundedRectangle(cornerRadius: 3, style: .continuous))
            .frame(width: size, height: size)
            .background(.white, in: shape)
            .overlay(alignment: .bottomTrailing) {
                if staged {
                    Circle().fill(Color.orange)
                        .frame(width: 8, height: 8)
                        .overlay(Circle().strokeBorder(.white, lineWidth: 1.5))
                        .padding(2)
                }
            }
            .shadow(color: .black.opacity(0.32), radius: 4, y: 2)
            .accessibilityHidden(true)
    }
}

private final class PhotoMapAnnotationView: MKAnnotationView {
    static let size: CGFloat = 44
    private let cardLayer = CALayer()
    private let imageLayer = CALayer()
    private let stagedDot = CALayer()
    private var currentURL: URL?

    override init(annotation: (any MKAnnotation)?, reuseIdentifier: String?) {
        super.init(annotation: annotation, reuseIdentifier: reuseIdentifier)
        frame.size = NSSize(width: Self.size, height: Self.size)
        wantsLayer = true
        clusteringIdentifier = "PhotoCluster"

        cardLayer.frame = CGRect(x: 0, y: 0, width: Self.size, height: Self.size)
        cardLayer.backgroundColor = NSColor.white.cgColor
        cardLayer.cornerRadius = 6
        cardLayer.shadowColor = NSColor.black.cgColor
        cardLayer.shadowOpacity = 0.32
        cardLayer.shadowRadius = 4
        cardLayer.shadowOffset = CGSize(width: 0, height: -2)

        imageLayer.frame = CGRect(x: 3, y: 3, width: Self.size - 6, height: Self.size - 6)
        imageLayer.backgroundColor = NSColor.controlBackgroundColor.cgColor
        imageLayer.cornerRadius = 3
        imageLayer.masksToBounds = true
        imageLayer.contentsGravity = .resizeAspectFill
        cardLayer.addSublayer(imageLayer)

        stagedDot.frame = CGRect(x: Self.size - 10, y: 2, width: 8, height: 8)
        stagedDot.backgroundColor = NSColor.systemOrange.cgColor
        stagedDot.cornerRadius = 4
        stagedDot.borderColor = NSColor.white.cgColor
        stagedDot.borderWidth = 1.5
        stagedDot.isHidden = true
        cardLayer.addSublayer(stagedDot)

        layer?.addSublayer(cardLayer)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func prepareForReuse() {
        super.prepareForReuse()
        currentURL = nil
        imageLayer.contents = nil
        stagedDot.isHidden = true
    }

    func configure(_ pin: WorkspaceMapView.MediaAnnotation) {
        annotation = pin
        clusteringIdentifier = "PhotoCluster"
        stagedDot.isHidden = !pin.staged
        displayPriority = .defaultHigh
        collisionMode = .rectangle
        let label = pin.title ?? ""
        toolTip = pin.staged ? label + " · Pending location" : label
        setAccessibilityLabel(toolTip)

        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        cardLayer.contentsScale = scale
        imageLayer.contentsScale = scale

        guard let url = pin.url else {
            currentURL = nil
            imageLayer.contents = nil
            return
        }
        guard currentURL != url || imageLayer.contents == nil else { return }
        currentURL = url
        if let cached = ThumbnailStore.shared.cache.object(forKey: url as NSURL) {
            imageLayer.contents = cached
            return
        }
        imageLayer.contents = nil
        ThumbnailStore.shared.load(url) { [weak self] loaded in
            guard let self, self.currentURL == url else { return }
            self.imageLayer.contents = loaded
        }
    }
}

private final class PhotoClusterAnnotationView: MKAnnotationView {
    static let size: CGFloat = 44
    static let viewSize: CGFloat = 52
    private let cardLayer = CALayer()
    private let imageLayer = CALayer()
    private let stagedDot = CALayer()
    private let badgeView = NSView()
    private let badgeLabel = NSTextField(labelWithString: "")
    private var currentURL: URL?

    override init(annotation: (any MKAnnotation)?, reuseIdentifier: String?) {
        super.init(annotation: annotation, reuseIdentifier: reuseIdentifier)
        frame.size = NSSize(width: Self.viewSize, height: Self.viewSize)
        centerOffset = NSPoint(x: 4, y: -4)
        wantsLayer = true

        cardLayer.frame = CGRect(x: 0, y: 0, width: Self.size, height: Self.size)
        cardLayer.backgroundColor = NSColor.white.cgColor
        cardLayer.cornerRadius = 6
        cardLayer.shadowColor = NSColor.black.cgColor
        cardLayer.shadowOpacity = 0.32
        cardLayer.shadowRadius = 4
        cardLayer.shadowOffset = CGSize(width: 0, height: -2)

        imageLayer.frame = CGRect(x: 3, y: 3, width: Self.size - 6, height: Self.size - 6)
        imageLayer.backgroundColor = NSColor.controlBackgroundColor.cgColor
        imageLayer.cornerRadius = 3
        imageLayer.masksToBounds = true
        imageLayer.contentsGravity = .resizeAspectFill
        cardLayer.addSublayer(imageLayer)

        stagedDot.frame = CGRect(x: Self.size - 10, y: 2, width: 8, height: 8)
        stagedDot.backgroundColor = NSColor.systemOrange.cgColor
        stagedDot.cornerRadius = 4
        stagedDot.borderColor = NSColor.white.cgColor
        stagedDot.borderWidth = 1.5
        stagedDot.isHidden = true
        cardLayer.addSublayer(stagedDot)

        layer?.addSublayer(cardLayer)

        let accent = MacAccent()
        badgeView.wantsLayer = true
        badgeView.layer?.backgroundColor = accent.selectionBackground(active: true).cgColor
        badgeView.layer?.cornerRadius = 9
        badgeView.layer?.borderColor = NSColor.white.cgColor
        badgeView.layer?.borderWidth = 0
        badgeView.layer?.shadowColor = NSColor.black.cgColor
        badgeView.layer?.shadowOpacity = 0.25
        badgeView.layer?.shadowRadius = 2
        badgeView.layer?.shadowOffset = CGSize(width: 0, height: -1)

        badgeLabel.font = .systemFont(ofSize: 10.5, weight: .bold)
        badgeLabel.textColor = accent.selectionForeground(active: true)
        badgeLabel.alignment = .center
        badgeView.addSubview(badgeLabel)
        addSubview(badgeView)
    }

    @available(*, unavailable)
    required init?(coder: NSCoder) { fatalError("init(coder:) has not been implemented") }

    override func prepareForReuse() {
        super.prepareForReuse()
        currentURL = nil
        imageLayer.contents = nil
        stagedDot.isHidden = true
    }

    func configure(_ cluster: MKClusterAnnotation, accent: MacAccent) {
        annotation = cluster
        displayPriority = .defaultHigh
        collisionMode = .rectangle

        let members = cluster.memberAnnotations.compactMap { $0 as? WorkspaceMapView.MediaAnnotation }
        let count = members.count
        let text = count > 99 ? "99+" : "\(count)"
        badgeView.layer?.backgroundColor = accent.selectionBackground(active: true).cgColor
        badgeLabel.textColor = accent.selectionForeground(active: true)
        badgeLabel.stringValue = text
        badgeLabel.sizeToFit()

        let badgeHeight: CGFloat = 18
        let badgeWidth = max(badgeHeight, ceil(badgeLabel.frame.width) + 10)
        badgeView.frame = NSRect(x: Self.viewSize - badgeWidth, y: isFlipped ? 0 : Self.viewSize - badgeHeight, width: badgeWidth, height: badgeHeight)
        badgeLabel.frame = NSRect(
            x: 0,
            y: floor((badgeHeight - badgeLabel.frame.height) / 2),
            width: badgeWidth,
            height: badgeLabel.frame.height
        )

        let anyStaged = members.contains(where: \.staged)
        stagedDot.isHidden = !anyStaged
        toolTip = "\(count) photos" + (anyStaged ? " · Pending location" : "")
        setAccessibilityLabel(toolTip)

        let scale = window?.backingScaleFactor ?? NSScreen.main?.backingScaleFactor ?? 2
        cardLayer.contentsScale = scale
        imageLayer.contentsScale = scale

        guard let url = (members.first(where: \.staged) ?? members.first)?.url else {
            currentURL = nil
            imageLayer.contents = nil
            return
        }
        guard currentURL != url || imageLayer.contents == nil else { return }
        currentURL = url
        if let cached = ThumbnailStore.shared.cache.object(forKey: url as NSURL) {
            imageLayer.contents = cached
            return
        }
        imageLayer.contents = nil
        ThumbnailStore.shared.load(url) { [weak self] loaded in
            guard let self, self.currentURL == url else { return }
            self.imageLayer.contents = loaded
        }
    }
}

func visibleInspectionTag(_ key: String) -> Bool {
    if key.hasPrefix("System:") {
        return ["System:FileSize", "System:FileCreateDate", "System:FileModifyDate"].contains(key)
    }
    return key != "SourceFile" && !key.hasPrefix("ExifTool:")
}

func aggregateMetadata(_ documents: [[String: Any]], referenceDocuments: [[String: Any]] = []) -> [MetadataRow] {
    // Reference files contribute available tags, never values for the edit targets.
    let keys = (documents + referenceDocuments).reduce(into: Set<String>()) { $0.formUnion($1.keys) }
    let otherRows = keys.filter { cameraTagName($0) == nil && visibleInspectionTag($0) }.sorted().map { key in
        let values = documents.map { $0[key].map { String(describing: $0) } ?? "—" }
        let mixed = Set(values).count > 1
        let value = mixed ? "Multiple values" : values.first ?? "—"
        let parts = key.components(separatedBy: ":")
        return MetadataRow(id: key, name: parts.last ?? key, group: parts.first ?? "", original: value, exported: value, mixed: mixed)
    }
    return (otherRows + mergedCameraRows(documents)).sorted { $0.id < $1.id }
}

extension Library {
    func readLocation(_ url: URL) -> LocationPin? {
        let (_, text) = run(["-j", "-n", "-GPSLatitude", "-GPSLongitude", url.path])
        func number(_ value: Any?) -> Double? {
            if let value = value as? NSNumber { return value.doubleValue }
            if let value = value as? String { return Double(value) }
            return nil
        }
        guard let data = text.data(using: .utf8),
              let rows = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]],
              let row = rows.first,
              let latitude = number(row["GPSLatitude"]),
              let longitude = number(row["GPSLongitude"]),
              latitude.isFinite, longitude.isFinite,
              (-90...90).contains(latitude), (-180...180).contains(longitude) else { return nil }
        return LocationPin(id: url.path, title: url.lastPathComponent, coordinate: CLLocationCoordinate2D(latitude: latitude, longitude: longitude))
    }

    func writeLocation(_ coordinate: CLLocationCoordinate2D, to url: URL, alongside other: [String]) -> (Int32, String) {
        guard CLLocationCoordinate2DIsValid(coordinate) else { return (1, "Invalid coordinates.") }
        let latitude = coordinate.latitude
        let longitude = coordinate.longitude
        let videoContainers: Set<String> = ["mp4", "mov", "m4v", "3gp", "3g2", "qt"]
        func formatted(_ value: Double) -> String {
            String(format: "%.7f", locale: Locale(identifier: "en_US_POSIX"), value)
        }
        let operations: [String]
        if videoContainers.contains(url.pathExtension.lowercased()) {
            operations = ["-Keys:GPSCoordinates#=\(formatted(latitude)) \(formatted(longitude))"]
        } else {
            operations = [
                "-EXIF:GPSLatitude#=\(formatted(abs(latitude)))",
                "-EXIF:GPSLatitudeRef=\(latitude < 0 ? "S" : "N")",
                "-EXIF:GPSLongitude#=\(formatted(abs(longitude)))",
                "-EXIF:GPSLongitudeRef=\(longitude < 0 ? "W" : "E")"
            ]
        }
        let (status, output) = self.executeMetadataWrite(other + operations, to: url)
        guard status == 0, !output.contains("Warning:"), !output.contains("Error:") else { return (status, output) }
        let verified = self.readLocation(url).map {
            abs($0.coordinate.latitude - latitude) < 0.000001 && abs($0.coordinate.longitude - longitude) < 0.000001
        } ?? false
        return verified ? (0, output + "\nGPS coordinates verified.") :
            (1, output + "\nCould not verify the GPS coordinates after writing.")
    }
}

struct WorkspaceMapView: NSViewRepresentable {
    let pins: [LocationPin]
    let searchSelection: LocationPin?
    var style = WorkspaceMapStyle.standard
    var fitRequest: UUID? = nil
    var isActive: Bool = true
    var accent = MacAccent()
    let onSelect: (Set<String>) -> Void
    let onPick: (CLLocationCoordinate2D) -> Void

    func makeCoordinator() -> Coordinator { Coordinator(onPick: onPick) }

    func makeNSView(context: Context) -> MKMapView {
        let map = MKMapView()
        map.delegate = context.coordinator
        map.showsZoomControls = true
        map.showsCompass = true
        map.isPitchEnabled = false
        let click = NSClickGestureRecognizer(target: context.coordinator, action: #selector(Coordinator.pick(_:)))
        click.delegate = context.coordinator
        map.addGestureRecognizer(click)
        map.setAccessibilityLabel("Location picker map")
        context.coordinator.update(pins: pins, on: map)
        if let first = pins.first, pins.allSatisfy({ $0.coordinate.latitude == first.coordinate.latitude && $0.coordinate.longitude == first.coordinate.longitude }) {
            map.setRegion(MKCoordinateRegion(center: first.coordinate, latitudinalMeters: 2600, longitudinalMeters: 2600), animated: false)
        } else if pins.isEmpty {
            map.setRegion(MKCoordinateRegion(center: CLLocationCoordinate2D(latitude: 20, longitude: 0), span: MKCoordinateSpan(latitudeDelta: 115, longitudeDelta: 145)), animated: false)
        } else {
            map.showAnnotations(map.annotations, animated: false)
        }
        return map
    }

    func updateNSView(_ view: MKMapView, context: Context) {
        guard isActive else { return }
        let coordinator = context.coordinator
        coordinator.onPick = onPick
        coordinator.onSelect = onSelect
        coordinator.accent = accent
        if view.mapType != style.type { view.mapType = style.type }
        let wasEmpty = coordinator.mediaPins.isEmpty
        coordinator.update(pins: pins, on: view)
        if wasEmpty && !pins.isEmpty && searchSelection == nil { view.showAnnotations(coordinator.mediaPins, animated: false) }
        if let fitRequest, fitRequest != coordinator.lastFit, !coordinator.mediaPins.isEmpty {
            coordinator.lastFit = fitRequest
            view.showAnnotations(coordinator.mediaPins, animated: !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
            return
        }
        guard let place = searchSelection, context.coordinator.lastSearchSelection != place.id else { return }
        context.coordinator.lastSearchSelection = place.id
        view.setRegion(MKCoordinateRegion(center: place.coordinate, latitudinalMeters: 1500, longitudinalMeters: 1500),
                       animated: !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
    }

    final class MediaAnnotation: MKPointAnnotation {
        let id: String
        let staged: Bool
        let url: URL?
        init(_ pin: LocationPin) {
            id = pin.id
            staged = pin.staged
            url = pin.fileURL
            super.init()
            coordinate = pin.coordinate
            title = pin.title
        }
    }

    final class Coordinator: NSObject, MKMapViewDelegate, NSGestureRecognizerDelegate {
        var onPick: (CLLocationCoordinate2D) -> Void
        var onSelect: ((Set<String>) -> Void)?
        var accent = MacAccent()
        var lastFit: UUID?
        var mediaPins: [MediaAnnotation] = []
        private var pinSignature: [String] = []

        func update(pins: [LocationPin], on map: MKMapView) {
            let signature = pins.map { "\($0.id)|\($0.coordinate.latitude)|\($0.coordinate.longitude)|\($0.staged)" }
            guard signature != pinSignature else { return }
            pinSignature = signature
            map.removeAnnotations(mediaPins)
            mediaPins = pins.map(MediaAnnotation.init)
            map.addAnnotations(mediaPins)
        }
        var lastSearchSelection: String?

        init(onPick: @escaping (CLLocationCoordinate2D) -> Void) {
            self.onPick = onPick
        }

        func gestureRecognizer(_ gestureRecognizer: NSGestureRecognizer, shouldRecognizeSimultaneouslyWith otherGestureRecognizer: NSGestureRecognizer) -> Bool {
            true
        }

        private func handleClusterTap(_ cluster: MKClusterAnnotation, on map: MKMapView) {
            let members = cluster.memberAnnotations.compactMap { $0 as? MediaAnnotation }
            guard !members.isEmpty else { return }
            if let minLat = members.map(\.coordinate.latitude).min(),
               let maxLat = members.map(\.coordinate.latitude).max(),
               let minLon = members.map(\.coordinate.longitude).min(),
               let maxLon = members.map(\.coordinate.longitude).max(),
               (maxLat - minLat) > 0.0002 || (maxLon - minLon) > 0.0002 {
                map.showAnnotations(members, animated: !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion)
            } else {
                onSelect?(Set(members.map(\.id)))
            }
        }

        @objc func pick(_ gesture: NSClickGestureRecognizer) {
            guard gesture.state == .ended, let map = gesture.view as? MKMapView else { return }
            let point = gesture.location(in: map)
            // Leave the map's native zoom and compass controls to MapKit.
            var hitView = map.hitTest(map.convert(point, to: map.superview))
            while let hit = hitView, hit !== map {
                if let cluster = (hit as? PhotoClusterAnnotationView)?.annotation as? MKClusterAnnotation {
                    handleClusterTap(cluster, on: map)
                    return
                }
                if let annotation = (hit as? PhotoMapAnnotationView)?.annotation as? MediaAnnotation {
                    onSelect?([annotation.id])
                    return
                }
                if hit is NSControl { return }
                hitView = hit.superview
            }
            for annotation in map.annotations {
                guard let view = map.view(for: annotation), !view.isHidden, view.alphaValue > 0,
                      view.bounds.contains(view.convert(point, from: map)) else { continue }
                if let cluster = annotation as? MKClusterAnnotation {
                    handleClusterTap(cluster, on: map)
                    return
                } else if let pin = annotation as? MediaAnnotation {
                    onSelect?([pin.id])
                    return
                }
            }
            let coordinate = map.convert(point, toCoordinateFrom: map)
            guard CLLocationCoordinate2DIsValid(coordinate) else { return }
            onPick(coordinate)
        }

        func mapView(_ mapView: MKMapView, viewFor annotation: MKAnnotation) -> MKAnnotationView? {
            if let cluster = annotation as? MKClusterAnnotation {
                let view = mapView.dequeueReusableAnnotationView(withIdentifier: "PhotoCluster") as? PhotoClusterAnnotationView ??
                    PhotoClusterAnnotationView(annotation: cluster, reuseIdentifier: "PhotoCluster")
                view.configure(cluster, accent: accent)
                return view
            }
            guard let pin = annotation as? MediaAnnotation else { return nil }
            let view = mapView.dequeueReusableAnnotationView(withIdentifier: "PhotoLocation") as? PhotoMapAnnotationView ??
                PhotoMapAnnotationView(annotation: pin, reuseIdentifier: "PhotoLocation")
            view.configure(pin)
            return view
        }
    }
}

struct LocationMap: View {
    let pins: [LocationPin]
    @State private var region = MKCoordinateRegion()

    var body: some View {
        Group {
            if pins.isEmpty {
                Label("No GPS coordinates in the selected files", systemImage: "map")
                    .font(.caption).foregroundStyle(.secondary).padding(.vertical, 12)
            } else {
                Map(coordinateRegion: $region, annotationItems: pins) { pin in
                    MapAnnotation(coordinate: pin.coordinate) {
                        PhotoMapPin(url: pin.fileURL, staged: pin.staged, size: 34)
                    }
                }
                .frame(height: 170)
                .clipShape(RoundedRectangle(cornerRadius: 8))
                .onAppear(perform: fitPins)
                .onChange(of: pins.map { "\($0.id):\($0.coordinate.latitude):\($0.coordinate.longitude)" }) { _ in fitPins() }
            }
        }
    }

    private func fitPins() {
        guard let first = pins.first else { return }
        var rect = MKMapRect.null
        for pin in pins {
            let point = MKMapPoint(pin.coordinate)
            rect = rect.union(MKMapRect(x: point.x, y: point.y, width: 1, height: 1))
        }
        if pins.count == 1 {
            region = MKCoordinateRegion(center: first.coordinate, span: MKCoordinateSpan(latitudeDelta: 0.015, longitudeDelta: 0.015))
        } else {
            let fitted = MKCoordinateRegion(rect)
            region = MKCoordinateRegion(center: fitted.center, span: MKCoordinateSpan(latitudeDelta: min(170, max(0.015, fitted.span.latitudeDelta * 1.3)), longitudeDelta: min(360, max(0.015, fitted.span.longitudeDelta * 1.3))))
        }
    }
}
