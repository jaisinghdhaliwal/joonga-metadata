import AppKit
import Foundation
import UniformTypeIdentifiers

struct WorkflowArchive: Codable {
    let format: String
    let version: Int
    let presets: [Preset]

    init(presets: [Preset]) {
        format = "MetadataTransferWorkflows"
        version = 1
        self.presets = presets
    }

    static func decode(_ data: Data) throws -> [Preset] {
        let decoder = JSONDecoder()
        let presets: [Preset]
        if let legacy = try? decoder.decode([Preset].self, from: data) {
            presets = legacy
        } else {
            let archive = try decoder.decode(WorkflowArchive.self, from: data)
            guard archive.format == "MetadataTransferWorkflows", archive.version == 1 else {
                throw TransferError.invalid("This workflow file uses an unsupported format or version.")
            }
            presets = archive.presets
        }
        for preset in presets {
            guard !preset.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty,
                  !preset.operations.isEmpty,
                  preset.operations.allSatisfy({
                      $0.range(of: "^-[A-Za-z][A-Za-z0-9:#-]*(=|<|>)", options: .regularExpression) != nil && !$0.contains("\n")
                  }) else {
                throw TransferError.invalid("The workflow “\(preset.name)” contains invalid metadata operations.")
            }
        }
        return presets
    }
}

enum TransferError: LocalizedError {
    case invalid(String)
    var errorDescription: String? {
        switch self { case .invalid(let message): return message }
    }
}

extension Library {
    func mergePresets(_ incoming: [Preset]) -> Int {
        var updated = presets
        var added = 0
        for var preset in incoming {
            let duplicate = updated.contains {
                $0.name == preset.name && $0.category == preset.category && $0.operations == preset.operations
            }
            guard !duplicate else { continue }
            preset.id = "personal-" + UUID().uuidString
            updated.append(preset)
            added += 1
        }
        return persistPresets(updated) ? added : -1
    }

    func importWorkflows() -> String {
        let panel = NSOpenPanel()
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = true
        panel.allowedContentTypes = [.json]
        panel.prompt = "Import Workflows"
        guard panel.runModal() == .OK else { return "" }
        do {
            // Validate every selected file before changing the library.
            let incoming = try panel.urls.flatMap { try WorkflowArchive.decode(Data(contentsOf: $0)) }
            let added = mergePresets(incoming)
            guard added >= 0 else { return "Import could not be saved. Check the workflow library folder permissions." }
            return "Imported \(added) workflows. Skipped \(incoming.count - added) duplicates."
        } catch {
            return "Import failed. \(error.localizedDescription)"
        }
    }

    func exportWorkflows(_ workflows: [Preset]) -> String {
        guard !workflows.isEmpty else { return "Select workflows to export." }
        let panel = NSSavePanel()
        panel.allowedContentTypes = [.json]
        panel.nameFieldStringValue = workflows.count == 1 ? "Workflow.json" : "Workflow Library.json"
        panel.prompt = "Export Workflows"
        guard panel.runModal() == .OK, let url = panel.url else { return "" }
        do {
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(WorkflowArchive(presets: workflows)).write(to: url, options: .atomic)
            return "Exported \(workflows.count) workflows."
        } catch {
            return "Export failed. \(error.localizedDescription)"
        }
    }
}
