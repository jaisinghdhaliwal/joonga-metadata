import Foundation

enum JoongaTagStorage {
    private static let support = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0]
    static let workflows = support.appendingPathComponent("Joonga Tag/Workflows.json")
    static let legacyWorkflows = support.appendingPathComponent("Metadata Transfer/Workflows.json")

    static func migratePreferences() {
        let defaults = UserDefaults.standard
        guard !defaults.bool(forKey: "DidMigrateJoongaMetadataPreferences"),
              let domain = Bundle.main.bundleIdentifier,
              domain == "com.jaisinghdhaliwal.joongatag" else { return }
        let previous = defaults.persistentDomain(forName: "local.codex.metadatapreview") ?? [:]
        var current = defaults.persistentDomain(forName: domain) ?? [:]
        current.merge(previous) { latest, _ in latest }
        current["DidMigrateJoongaMetadataPreferences"] = true
        defaults.setPersistentDomain(current, forName: domain)
    }
}
