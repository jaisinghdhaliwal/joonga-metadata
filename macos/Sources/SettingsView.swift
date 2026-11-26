import AppKit
import SwiftUI

extension Library {
    private static let customExifToolDefaultsKey = "CustomExifToolPath"

    var workflowDirectory: URL { workflowFile.deletingLastPathComponent() }

    var bundledExifToolPath: String? {
        guard let url = Bundle.main.resourceURL?.appendingPathComponent("exiftool/exiftool") else { return nil }
        let path = url.standardizedFileURL.path
        return isExecutableFile(atPath: path) ? path : nil
    }

    var systemExifToolPath: String? {
        ["/opt/homebrew/bin/exiftool", "/usr/local/bin/exiftool", "/usr/bin/exiftool"]
            .first { isExecutableFile(atPath: $0) }
    }

    var isUsingBundledExifTool: Bool {
        guard let bundled = bundledExifToolPath, let active = exif else { return false }
        return URL(fileURLWithPath: active).standardizedFileURL.path == bundled
    }

    func configureExifTool() {
        let stored = JoongaTagStorage.string(forKey: Self.customExifToolDefaultsKey)?
            .trimmingCharacters(in: .whitespacesAndNewlines)
        applyExifToolConfiguration(
            customPath: stored?.isEmpty == false ? stored : nil,
            validateExecutable: false,
            persist: false,
            reloadMedia: false
        )
    }

    func chooseExifTool() {
        guard !busy, !mediaLoading else { return }
        let panel = NSOpenPanel()
        panel.canChooseFiles = true
        panel.canChooseDirectories = false
        panel.allowsMultipleSelection = false
        panel.showsHiddenFiles = true
        panel.treatsFilePackagesAsDirectories = true
        panel.message = "Choose an ExifTool executable (for example, /opt/homebrew/bin/exiftool)."
        panel.prompt = "Use ExifTool"
        if let current = customExifToolPath ?? exif {
            panel.directoryURL = URL(fileURLWithPath: current).deletingLastPathComponent()
        } else if FileManager.default.fileExists(atPath: "/opt/homebrew/bin") {
            panel.directoryURL = URL(fileURLWithPath: "/opt/homebrew/bin")
        }
        if panel.runModal() == .OK, let url = panel.url {
            setCustomExifTool(url.path)
        }
    }

    @discardableResult
    func setCustomExifTool(_ rawPath: String) -> Bool {
        guard !busy, !mediaLoading else { return false }
        let trimmed = rawPath.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else {
            exifToolError = "Enter a path to an ExifTool executable or restore the bundled ExifTool."
            return false
        }
        return applyExifToolConfiguration(customPath: trimmed, validateExecutable: true, persist: true, reloadMedia: true)
    }

    func restoreBundledExifTool() {
        guard !busy, !mediaLoading else { return }
        applyExifToolConfiguration(customPath: nil, validateExecutable: false, persist: true, reloadMedia: true)
    }

    private func isExecutableFile(atPath path: String) -> Bool {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: path, isDirectory: &isDirectory), !isDirectory.boolValue else {
            return false
        }
        return FileManager.default.isExecutableFile(atPath: path)
    }

    private func probeExifToolVersion(atPath path: String) -> String? {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: path)
        process.arguments = ["-ver"]
        let pipe = Pipe()
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = pipe
        process.standardError = pipe
        do {
            try process.run()
            let data = pipe.fileHandleForReading.readDataToEndOfFile()
            process.waitUntilExit()
            guard process.terminationStatus == 0 else { return nil }
            let version = (String(data: data, encoding: .utf8) ?? "")
                .trimmingCharacters(in: .whitespacesAndNewlines)
            guard !version.isEmpty, version.range(of: "^[0-9]+(\\.[0-9]+)+$", options: .regularExpression) != nil else {
                return nil
            }
            return version
        } catch {
            return nil
        }
    }

    @discardableResult
    private func applyExifToolConfiguration(
        customPath: String?,
        validateExecutable: Bool,
        persist: Bool,
        reloadMedia: Bool
    ) -> Bool {
        if let customPath {
            let expanded = (customPath as NSString).expandingTildeInPath
            let normalized = URL(fileURLWithPath: expanded).standardizedFileURL.path
            if normalized == bundledExifToolPath {
                return applyExifToolConfiguration(
                    customPath: nil,
                    validateExecutable: false,
                    persist: persist,
                    reloadMedia: reloadMedia
                )
            }
            let executable = isExecutableFile(atPath: normalized)
            if validateExecutable {
                guard executable else {
                    exifToolError = "No executable ExifTool file was found at \(normalized)."
                    return false
                }
                guard let probedVersion = probeExifToolVersion(atPath: normalized) else {
                    exifToolError = "The selected file at \(normalized) did not run as ExifTool."
                    return false
                }
                if persist {
                    JoongaTagStorage.set(normalized, forKey: Self.customExifToolDefaultsKey)
                }
                updateActiveExifTool(path: normalized, customPath: normalized, version: probedVersion, error: nil, reloadMedia: reloadMedia)
                return true
            }
            if !executable {
                updateActiveExifTool(
                    path: nil,
                    customPath: normalized,
                    version: nil,
                    error: "Custom ExifTool was not found at \(normalized).",
                    reloadMedia: reloadMedia
                )
                return false
            }
            if persist {
                JoongaTagStorage.set(normalized, forKey: Self.customExifToolDefaultsKey)
            }
            updateActiveExifTool(path: normalized, customPath: normalized, version: nil, error: nil, reloadMedia: reloadMedia)
            refreshExifToolVersion(for: normalized)
            return true
        }

        if persist {
            JoongaTagStorage.removeObject(forKey: Self.customExifToolDefaultsKey)
        }
        let resolved = bundledExifToolPath ?? systemExifToolPath
        let error = resolved == nil ? "Bundled ExifTool was not found." : nil
        updateActiveExifTool(path: resolved, customPath: nil, version: nil, error: error, reloadMedia: reloadMedia)
        if let resolved {
            refreshExifToolVersion(for: resolved)
        }
        return resolved != nil
    }

    private func refreshExifToolVersion(for path: String) {
        DispatchQueue.global(qos: .utility).async {
            let version = self.probeExifToolVersion(atPath: path)
            DispatchQueue.main.async {
                if self.exif == path {
                    self.exifToolVersion = version
                }
            }
        }
    }

    private func updateActiveExifTool(
        path: String?,
        customPath: String?,
        version: String?,
        error: String?,
        reloadMedia: Bool
    ) {
        setActiveExifPath(path)
        customExifToolPath = customPath
        exif = path
        exifToolVersion = version
        exifToolError = error
        if path == nil {
            message = "Open Settings to restore or choose ExifTool."
        } else if message.hasPrefix("Open Settings to restore") || message.hasPrefix("ExifTool is required") {
            message = ""
        }
        if reloadMedia, path != nil {
            inspect()
            if !mediaItems.isEmpty {
                refreshMedia(mediaItems.map(\.id))
            }
        }
    }

    func openFolder(_ url: URL) {
        guard (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true else {
            message = "Folder is unavailable. Reconnect the drive or choose another folder."
            return
        }
        NSWorkspace.shared.open(url)
    }

    func openOrChooseFolder(originals: Bool) {
        if let folder = originals ? source : destination {
            openFolder(folder)
        } else if !busy {
            choose(originals)
        }
    }

    func chooseDefaultExports() {
        let panel = NSOpenPanel()
        panel.canChooseFiles = false
        panel.canChooseDirectories = true
        panel.directoryURL = defaultExports ?? destination
        panel.prompt = "Set Default Folder"
        if panel.runModal() == .OK, let folder = panel.url {
            storeDefaultExports(folder)
        }
    }

    func openWorkflowDirectory() {
        do {
            try FileManager.default.createDirectory(at: workflowDirectory, withIntermediateDirectories: true)
            openFolder(workflowDirectory)
        } catch {
            NSAlert(error: error).runModal()
        }
    }
}

struct SettingsView: View {
    @ObservedObject var library: Library
    @Environment(\.dismiss) private var dismiss
    @Environment(\.joongaAccent) private var accent
    @State private var showPreviewOnLaunch = JoongaTagStorage.object(forKey: "ShowPreviewOnLaunch") as? Bool ?? true
    @State private var confirmingUninstall = false
    @State private var uninstallError: String?
    @State private var exifToolInput = ""

    private var trimmedExifInput: String {
        exifToolInput.trimmingCharacters(in: .whitespacesAndNewlines)
    }

    private var currentExifDisplayPath: String {
        library.customExifToolPath ?? library.exif ?? ""
    }

    private var exifStatusText: String {
        if let error = library.exifToolError { return error }
        let versionSuffix = library.exifToolVersion.map { " (ExifTool \($0))" } ?? ""
        if library.isUsingBundledExifTool { return "Using app-bundled ExifTool" + versionSuffix }
        if library.customExifToolPath != nil { return "Using custom ExifTool" + versionSuffix }
        if library.exif != nil { return "Using system ExifTool" + versionSuffix }
        return "ExifTool not found"
    }

    private var canRestoreBundled: Bool {
        guard !library.busy, !library.mediaLoading else { return false }
        if library.customExifToolPath != nil { return library.bundledExifToolPath != nil || library.systemExifToolPath != nil }
        return !library.isUsingBundledExifTool && library.bundledExifToolPath != nil
    }

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            HStack {
                Label("Settings", systemImage: "gearshape")
                    .font(.title3.weight(.semibold))
                    .accessibilityAddTraits(.isHeader)
                Spacer()
            }
            .padding(20)
            .background(.ultraThinMaterial)
            Divider()
            Form {
                Section("Workspace") {
                    Toggle("Show preview at launch", isOn: $showPreviewOnLaunch).tint(accent.tint)
                    LabeledContent("Default exports folder") {
                        VStack(alignment: .leading, spacing: 8) {
                            Text(library.defaultExports?.path ?? "No default folder")
                                .font(.callout)
                                .foregroundStyle(.secondary)
                                .lineLimit(2)
                                .truncationMode(.middle)
                                .textSelection(.enabled)
                                .help(library.defaultExports?.path ?? "")
                            HStack {
                                Button("Choose…") { library.chooseDefaultExports() }
                                Button("Open") {
                                    if let folder = library.defaultExports { library.openFolder(folder) }
                                }.disabled(library.defaultExports == nil)
                                Button("Clear") { library.clearDefaultExports() }
                                    .disabled(library.defaultExports == nil)
                            }
                            .buttonStyle(.bordered).tint(.primary)
                        }
                    }
                }
                Section("Tools and data") {
                    LabeledContent("ExifTool") {
                        VStack(alignment: .leading, spacing: 8) {
                            HStack(spacing: 6) {
                                TextField("Path to exiftool executable", text: $exifToolInput)
                                    .textFieldStyle(.roundedBorder)
                                    .font(.callout)
                                    .disabled(library.busy || library.mediaLoading)
                                    .help(currentExifDisplayPath)
                                    .onSubmit { library.setCustomExifTool(exifToolInput) }
                                if trimmedExifInput != currentExifDisplayPath {
                                    Button("Apply") { library.setCustomExifTool(exifToolInput) }
                                        .disabled(library.busy || library.mediaLoading || trimmedExifInput.isEmpty)
                                }
                            }
                            Text(exifStatusText)
                                .font(.caption)
                                .foregroundStyle(library.exif == nil || library.exifToolError != nil ? .orange : .secondary)
                            HStack {
                                Button("Choose…") { library.chooseExifTool() }
                                    .disabled(library.busy || library.mediaLoading)
                                Button("Use Bundled ExifTool") { library.restoreBundledExifTool() }
                                    .disabled(!canRestoreBundled)
                            }
                            .buttonStyle(.bordered).tint(.primary)
                        }
                    }
                    LabeledContent("Presets") {
                        Button("Open Library Folder") { library.openWorkflowDirectory() }
                    }
                }
                Section("Uninstall") {
                    Button("Uninstall Joonga Tag…", role: .destructive) { confirmingUninstall = true }
                        .disabled(library.busy || library.mediaLoading || !library.pendingPaths.isEmpty)
                    Text("Removes this copy of the app, settings, presets, and caches.")
                        .font(.caption).foregroundStyle(.secondary)
                    if let uninstallError { Text(uninstallError).foregroundStyle(.secondary) }
                }
            }
            .formStyle(.grouped)
            .scrollContentBackground(.hidden)
            Divider()
            HStack {
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }.padding(16).background(.ultraThinMaterial)
        }
        .frame(width: 600, height: 480)
        .background(Vibrancy(material: .underWindowBackground, blendingMode: .behindWindow))
        .onAppear { exifToolInput = currentExifDisplayPath }
        .onChange(of: library.exif) { _ in exifToolInput = currentExifDisplayPath }
        .onChange(of: library.customExifToolPath) { _ in exifToolInput = currentExifDisplayPath }
        .onChange(of: showPreviewOnLaunch) {
            JoongaTagStorage.set($0, forKey: "ShowPreviewOnLaunch")
            library.showInspector = $0
        }
        .confirmationDialog("Uninstall Joonga Tag?", isPresented: $confirmingUninstall, titleVisibility: .visible) {
            Button("Uninstall", role: .destructive) {
                do { try (NSApp.delegate as? App)?.uninstall() }
                catch { uninstallError = error.localizedDescription }
            }
        } message: {
            Text("This removes Joonga Tag’s settings and presets. Photos, videos, exports, and separately installed software are kept.")
        }
    }
}
