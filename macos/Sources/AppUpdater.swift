import AppKit
import CryptoKit
import SwiftUI

struct AboutJoongaView: View {
    let close: () -> Void
    let canInstall: () -> Bool
    let restart: () -> Void
    @Environment(\.joongaAccent) private var accent
    @State private var release: MacAppUpdater.Release?
    @State private var busy = false
    @State private var status = ""
    private let version = Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "0.0.0"

    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            Image(nsImage: NSApp.applicationIconImage).resizable().frame(width: 56, height: 56)
            Text("Joonga Metadata").font(.title2.bold())
            Text("Version " + version).foregroundStyle(.secondary)
            if !status.isEmpty {
                Text(status).font(.callout).textSelection(.enabled)
            }
            HStack {
                Button("Check for Updates") { check() }.disabled(busy)
                if let release {
                    Button("Update and Restart") { install(release) }
                        .buttonStyle(.borderedProminent).tint(accent.tint).disabled(busy)
                }
                Spacer()
                Button("Done", action: close).keyboardShortcut(.cancelAction).disabled(busy)
            }
        }
        .frame(width: 435, alignment: .leading)
        .padding(24)
    }

    private func check() {
        busy = true
        release = nil
        status = "Checking GitHub…"
        Task {
            defer { busy = false }
            do {
                release = try await MacAppUpdater.check(installed: version)
                status = release.map { "Version \($0.version) is available." } ?? "You’re up to date."
            } catch { status = "Update check failed. " + error.localizedDescription }
        }
    }

    private func install(_ release: MacAppUpdater.Release) {
        guard canInstall() else {
            status = "Save or revert pending edits and wait for current work to finish first."
            return
        }
        busy = true
        status = "Downloading and verifying the update…"
        Task {
            defer { busy = false }
            do {
                let destination = Bundle.main.bundleURL
                let identifier = Bundle.main.bundleIdentifier
                let staged = try await Task.detached {
                    try await MacAppUpdater.prepare(release, destination: destination, identifier: identifier)
                }.value
                do {
                    guard canInstall() else { throw MacAppUpdater.failure("Save or revert pending edits first.") }
                    try MacAppUpdater.launchSwap(staged: staged, destination: destination)
                } catch {
                    try? FileManager.default.removeItem(at: staged)
                    throw error
                }
                restart()
            } catch { status = "Update could not start. " + error.localizedDescription }
        }
    }
}

enum MacAppUpdater {
    private static let repository = "jaisinghdhaliwal/joonga-metadata"
    struct Release: Sendable {
        let version: String
        let zip: URL
        let digest: String
        let size: Int64
    }
    private struct GitHubRelease: Decodable {
        let tag_name: String
        let assets: [Asset]
        struct Asset: Decodable {
            let name: String
            let browser_download_url: URL
            let digest: String?
            let size: Int64
        }
    }

    static func failure(_ message: String) -> NSError {
        NSError(domain: "JoongaMetadata.Update", code: 1, userInfo: [NSLocalizedDescriptionKey: message])
    }

    static func check(installed: String) async throws -> Release? {
        let endpoint = URL(string: "https://api.github.com/repos/\(repository)/releases/latest")!
        var request = URLRequest(url: endpoint, timeoutInterval: 30)
        request.setValue("JoongaMetadata-Updater", forHTTPHeaderField: "User-Agent")
        let (data, response) = try await URLSession.shared.data(for: request)
        guard let http = response as? HTTPURLResponse, http.statusCode == 200 else {
            throw failure("GitHub did not return the latest release.")
        }
        let release = try JSONDecoder().decode(GitHubRelease.self, from: data)
        let parts = release.tag_name.dropFirst().split(separator: ".", omittingEmptySubsequences: false)
        guard release.tag_name.hasPrefix("v"), parts.count == 3,
              parts.allSatisfy({ !$0.isEmpty && $0.utf8.allSatisfy { (48...57).contains($0) } }) else {
            throw failure("GitHub returned an unsupported version.")
        }
        let version = String(release.tag_name.dropFirst())
        guard version.compare(installed, options: .numeric) == .orderedDescending else { return nil }
        let name = "Joonga-Metadata-\(version)-macOS.zip"
        let expected = "https://github.com/\(repository)/releases/download/\(release.tag_name)/\(name)"
        guard let asset = release.assets.first(where: { $0.name == name && $0.browser_download_url.absoluteString == expected }) else {
            throw failure("This release has no macOS update archive.")
        }
        guard let digest = asset.digest, digest.hasPrefix("sha256:"), digest.count == 71,
              digest.dropFirst(7).utf8.allSatisfy({ (48...57).contains($0) || (97...102).contains($0) }) else {
            throw failure("GitHub has not published a SHA-256 digest for this archive.")
        }
        return Release(version: version, zip: asset.browser_download_url, digest: String(digest.dropFirst(7)), size: asset.size)
    }

    static func prepare(_ release: Release, destination: URL, identifier: String?) async throws -> URL {
        let files = FileManager.default
        let parent = destination.deletingLastPathComponent()
        guard destination.pathExtension == "app", files.isWritableFile(atPath: parent.path) else {
            throw failure("Move Joonga Metadata to a writable Applications folder and try again.")
        }
        guard release.size > 0, release.size <= 512 * 1024 * 1024 else { throw failure("Unexpected archive size.") }
        let stage = parent.appendingPathComponent(".joonga-update-" + UUID().uuidString, isDirectory: true)
        try files.createDirectory(at: stage, withIntermediateDirectories: false, attributes: [.posixPermissions: 0o700])
        do {
            let (download, response) = try await URLSession.shared.download(for: URLRequest(url: release.zip, timeoutInterval: 60))
            defer { try? files.removeItem(at: download) }
            guard let http = response as? HTTPURLResponse, http.statusCode == 200,
                  (try files.attributesOfItem(atPath: download.path)[.size] as? NSNumber)?.int64Value == release.size else {
                throw failure("The archive download is incomplete.")
            }
            let input = try FileHandle(forReadingFrom: download)
            defer { try? input.close() }
            var checksum = SHA256()
            while let data = try input.read(upToCount: 1024 * 1024), !data.isEmpty { checksum.update(data: data) }
            let actual = checksum.finalize().map { String(format: "%02x", $0) }.joined()
            guard actual == release.digest else { throw failure("Archive checksum did not match GitHub.") }
            try run("/usr/bin/ditto", ["-x", "-k", download.path, stage.path])
            let candidate = stage.appendingPathComponent("Joonga Metadata.app", isDirectory: true)
            guard let id = identifier, let bundle = Bundle(url: candidate), bundle.bundleIdentifier == id,
                  bundle.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String == release.version else {
                throw failure("The archive does not match this app and release.")
            }
            try run("/usr/bin/codesign", ["--verify", "--strict", "--all-architectures", candidate.path])
            return stage
        } catch {
            try? files.removeItem(at: stage)
            throw error
        }
    }

    private static func run(_ command: String, _ arguments: [String]) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: command)
        process.arguments = arguments
        try process.run()
        process.waitUntilExit()
        guard process.terminationStatus == 0 else { throw failure("Could not verify or unpack the macOS update.") }
    }

    static func launchSwap(staged: URL, destination: URL) throws {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/bin/sh")
        // The transient helper waits for the app to exit and keeps the previous
        // bundle available until the new one is in place.
        process.arguments = ["-c", """
          pid="$1"; stage="$2"; destination="$3"
          candidate="$stage/Joonga Metadata.app"; previous="$stage/Previous.app"
          attempts=0
          while kill -0 "$pid" 2>/dev/null; do
            attempts=$((attempts+1))
            [ "$attempts" -lt 30 ] || exit 1
            sleep 1
          done
          if /bin/mv "$destination" "$previous"; then
            if /bin/mv "$candidate" "$destination"; then
              /usr/bin/open "$destination" && /bin/rm -rf "$stage"
              exit
            fi
            /bin/mv "$previous" "$destination"
          fi
          /usr/bin/open "$destination"
          /usr/bin/osascript -e 'display alert "Joonga Metadata update could not be installed."'
          """, "joonga-update", String(ProcessInfo.processInfo.processIdentifier), staged.path, destination.path]
        try process.run()
    }
}
