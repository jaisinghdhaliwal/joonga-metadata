import Foundation

enum JoongaTagStorage {
    private static let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
    static let folder = support.appendingPathComponent("Joonga Tag", isDirectory: true)
    static let workflows = folder.appendingPathComponent("Workflows.json")
    static let settings = folder.appendingPathComponent("Settings.json")
    static let legacyWorkflows = support.appendingPathComponent("Metadata Transfer/Workflows.json")
    static let domain = "com.jaisinghdhaliwal.joongatag"

    private static var values: [String: Any] = load()

    static func loadSettings() { _ = values }

    private static func load() -> [String: Any] {
        let files = FileManager.default
        if files.fileExists(atPath: settings.path) {
            do {
                return try JSONSerialization.jsonObject(with: Data(contentsOf: settings)) as? [String: Any] ?? [:]
            } catch {
                NSLog("Could not read Joonga Tag settings: %@", error.localizedDescription)
                return [:]
            }
        }
        guard Bundle.main.bundleIdentifier == domain else { return [:] }
        let defaults = UserDefaults.standard
        let previous = defaults.persistentDomain(forName: "local.codex.metadatapreview") ?? [:]
        var current = defaults.persistentDomain(forName: domain) ?? [:]
        current.merge(previous) { latest, _ in latest }
        let keys: Set<String> = ["CustomExifToolPath", "ShowPreviewOnLaunch", "DefaultExportsBookmark",
            "MapFavourites", "WorkspaceMapStyle", "MapShowsSelectedOnly", "PersonalPresets"]
        let migrated = current.filter { keys.contains($0.key) || $0.key.hasPrefix("MediaColumns") }
            .mapValues { value in (value as? Data)?.base64EncodedString() as Any? ?? value }
        persist(migrated)
        return migrated
    }

    private static func persist(_ values: [String: Any]) {
        do {
            let data = try JSONSerialization.data(withJSONObject: values, options: [.prettyPrinted, .sortedKeys])
            try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
            try data.write(to: settings, options: .atomic)
        } catch {
            NSLog("Could not save Joonga Tag settings: %@", error.localizedDescription)
        }
    }

    static func object(forKey key: String) -> Any? { values[key] }
    static func string(forKey key: String) -> String? { values[key] as? String }
    static func stringArray(forKey key: String) -> [String]? { values[key] as? [String] }
    static func data(forKey key: String) -> Data? {
        guard let encoded = string(forKey: key) else { return nil }
        return Data(base64Encoded: encoded)
    }
    static func set(_ value: Any, forKey key: String) {
        values[key] = (value as? Data)?.base64EncodedString() as Any? ?? value
        persist(values)
    }
    static func removeObject(forKey key: String) {
        values.removeValue(forKey: key)
        persist(values)
    }
}
