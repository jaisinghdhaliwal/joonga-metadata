import Foundation

func cameraTagName(_ key: String) -> String? {
    let name = key.components(separatedBy: ":").last?.lowercased()
    if name == "make" { return "Make" }
    if name == "model" { return "Model" }
    return nil
}

func cameraValues(_ document: [String: Any], tag: String) -> Set<String> {
    Set(document.compactMap { key, value in
        cameraTagName(key) == tag ? String(describing: value) : nil
    })
}

func mergedCameraRows(_ documents: [[String: Any]]) -> [MetadataRow] {
    ["Make", "Model"].compactMap { tag in
        let perFile = documents.map { cameraValues($0, tag: tag) }
        guard !documents.isEmpty else { return nil }
        let values = perFile.reduce(into: Set<String>()) { result, fileValues in
            result.formUnion(fileValues.isEmpty ? ["—"] : fileValues)
        }
        let mixed = values.count > 1
        let value = mixed ? "Multiple values" : values.first ?? "—"
        return MetadataRow(id: tag, name: tag, group: "Camera", original: value, exported: value, mixed: mixed)
    }
}

func comparedCameraRows(original: [String: Any], exported: [String: Any]) -> [MetadataRow] {
    ["Make", "Model"].compactMap { tag in
        let a = cameraValues(original, tag: tag)
        let b = cameraValues(exported, tag: tag)
        func display(_ values: Set<String>) -> String {
            values.count > 1 ? "Multiple values" : values.first ?? "—"
        }
        return MetadataRow(id: tag, name: tag, group: "Camera", original: display(a), exported: display(b))
    }
}
