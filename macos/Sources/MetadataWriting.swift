import Foundation

extension Library {
    func executeMetadataWrite(_ operations: [String], to url: URL) -> (Int32, String) {
        var arguments = operations
        var expected = [String: String]()
        let videoContainers: Set<String> = ["mp4", "mov", "m4v", "3gp", "3g2", "qt"]
        for operation in operations {
            guard let separator = operation.firstIndex(of: "=") else { continue }
            let tag = String(operation[..<separator].dropFirst())
            let value = String(operation[operation.index(after: separator)...])
            let name = tag.components(separatedBy: ":").last?.lowercased() ?? ""
            guard name == "make" || name == "model" else { continue }
            expected[tag] = value
            if !tag.contains(":"), videoContainers.contains(url.pathExtension.lowercased()) {
                let nativeTag = name == "make" ? "Keys:Make" : "Keys:Model"
                arguments.append("-\(nativeTag)=\(value)")
            }
        }
        if !expected.isEmpty {
            let existing = read(url)
            let writableGroups: Set<String> = ["IFD0", "XMP-tiff", "Keys", "UserData", "ItemList"]
            for (tag, value) in expected where !tag.contains(":") {
                for key in existing.keys {
                    let parts = key.components(separatedBy: ":")
                    if parts.count == 2, writableGroups.contains(parts[0]),
                       parts[1].caseInsensitiveCompare(tag) == .orderedSame {
                        arguments.append("-\(key)=\(value)")
                    }
                }
            }
        }
        let (status, output) = run(["-P"] + arguments + [url.path])
        guard status == 0, !expected.isEmpty else { return (status, output) }
        let (_, readback) = run(["-j", "-a", "-G1", "-Make", "-Model", url.path])
        guard let data = readback.data(using: .utf8),
              let documents = try? JSONSerialization.jsonObject(with: data) as? [[String: Any]],
              let document = documents.first else {
            return (1, output + "\nVerification failed. Could not read camera metadata back from this file.\n")
        }
        var verified = true
        var details = [String]()
        for (requested, value) in expected.sorted(by: { $0.key < $1.key }) {
            let suffix = requested.components(separatedBy: ":").last!.lowercased()
            let matches = document.filter { key, _ in
                if requested.contains(":") { return key.caseInsensitiveCompare(requested) == .orderedSame }
                return key.components(separatedBy: ":").last?.lowercased() == suffix
            }
            let correct = value.isEmpty ? matches.isEmpty : !matches.isEmpty && matches.values.allSatisfy { String(describing: $0) == value }
            verified = verified && correct
            details.append("\(requested) — \(correct ? "verified" : "VERIFICATION FAILED")")
            details += matches.sorted(by: { $0.key < $1.key }).map { "  \($0.key) = \($0.value)" }
        }
        return (verified ? 0 : 1, output + "\nRead-back verification\n" + details.joined(separator: "\n") + "\n")
    }
}
