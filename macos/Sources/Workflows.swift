import AppKit
import SwiftUI

struct Preset: Identifiable, Codable, Hashable {
    var id: String
    var name: String
    var category: String
    var operations: [String]
}

extension Library {
    var editingURL: URL? {
        guard let pair = selected else { return nil }
        return editOriginal ? pair.original : pair.exported
    }

    var originalURLs: [URL] {
        Array(Set(files.compactMap(\.original) + unmatchedOriginals)).sorted { $0.path < $1.path }
    }

    var editingTargets: [URL] {
        if mode != .transfer || selectedTargets.count > 1 { return selectedTargets }
        return editingURL.map { [$0] } ?? []
    }

    var pendingEdits: [String: String] {
        var values = [String: String]()
        for url in editingTargets {
            for (key, value) in drafts[url.path] ?? [:] { values[key] = value }
        }
        return values
    }

    func stage(_ key: String, value: String) {
        guard !busy, exif != nil else { return }
        for url in editingTargets { drafts[url.path, default: [:]][key] = value }
    }

    func saveEdits() {
        guard !busy, !mediaLoading, exif != nil, !pendingPaths.isEmpty else { return }
        let changes = pendingPaths.sorted().map { path in
            let fields = drafts[path] ?? [:]
            let operations = (workflowDrafts[path] ?? []) + fields.keys.sorted().compactMap { key in fields[key].map { "-\(key)=\($0)" } }
            return (path, operations, locationDrafts[path])
        }
        busy = true
        DispatchQueue.global(qos: .userInitiated).async {
            var saved = [String](), messages = [String]()
            for (path, operations, point) in changes {
                let url = URL(fileURLWithPath: path)
                let (status, output) = point.map { self.writeLocation($0.coordinate, to: url, alongside: operations) }
                    ?? self.executeMetadataWrite(operations, to: url)
                if status == 0 && !output.contains("Warning:") && !output.contains("Error:") { saved.append(path) }
                messages.append(path + "\n" + output)
            }
            let paths = saved, result = messages.joined(separator: "\n")
            DispatchQueue.main.async {
                for path in paths { self.drafts[path] = nil; self.locationDrafts[path] = nil; self.workflowDrafts[path] = nil }
                self.busy = false
                self.message = "Saved \(paths.count) of \(changes.count) files"
                self.report = result
                self.inspect()
                self.refreshMedia(paths)
            }
        }
    }

    func writeMetadata(_ operations: [String], to urls: [URL], completion: @escaping () -> Void = {}) {
        guard !busy, !urls.isEmpty, !operations.isEmpty else { return }
        let pattern = "^-[A-Za-z][A-Za-z0-9:#-]*(=|<|>)"
        guard operations.allSatisfy({ $0.range(of: pattern, options: .regularExpression) != nil && !$0.contains("\n") }) else {
            report = "Use one metadata assignment or tag-copy operation per line."
            return
        }
        for url in urls { workflowDrafts[url.path, default: []].append(contentsOf: operations) }
        message = "Preset staged for \(urls.count) \(urls.count == 1 ? "file" : "files")"
        completion()
    }

    func loadPresets() {
        do {
            if FileManager.default.fileExists(atPath: workflowFile.path) {
                presets = try WorkflowArchive.decode(Data(contentsOf: workflowFile))
            } else if workflowFile == JoongaTagStorage.workflows,
                      FileManager.default.fileExists(atPath: JoongaTagStorage.legacyWorkflows.path) {
                let migrated = try WorkflowArchive.decode(Data(contentsOf: JoongaTagStorage.legacyWorkflows))
                if !persistPresets(migrated) { presets = migrated }
            } else if let data = JoongaTagStorage.data(forKey: "PersonalPresets") {
                let migrated = try JSONDecoder().decode([Preset].self, from: data)
                _ = persistPresets(migrated)
            }
        } catch {
            message = "Could not load presets. " + error.localizedDescription
        }
    }

    @discardableResult
    func persistPresets(_ updated: [Preset]) -> Bool {
        do {
            let directory = workflowFile.deletingLastPathComponent()
            try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true)
            let encoder = JSONEncoder()
            encoder.outputFormatting = [.prettyPrinted, .sortedKeys]
            try encoder.encode(WorkflowArchive(presets: updated)).write(to: workflowFile, options: .atomic)
            presets = updated
            return true
        } catch {
            report = "Could not save presets. " + error.localizedDescription
            return false
        }
    }

    @discardableResult
    func savePreset(_ preset: Preset) -> Bool {
        var updated = presets
        if let index = updated.firstIndex(where: { $0.id == preset.id }) { updated[index] = preset }
        else { updated.append(preset) }
        return persistPresets(updated)
    }

    @discardableResult
    func deletePresets(_ ids: Set<String>) -> Bool {
        persistPresets(presets.filter { !ids.contains($0.id) })
    }

}

struct MetadataValueEditor: View {
    @ObservedObject var library: Library
    @Environment(\.joongaAccent) private var accent
    let row: MetadataRow
    let tag: String

    private var storedRow: MetadataRow { library.metadata.first { $0.id == tag } ?? row }
    private var current: String {
        library.selectedTargets.count > 1 ? storedRow.exported : library.editOriginal ? storedRow.original : storedRow.exported
    }
    private var pendingValues: Set<String?> { Set(library.editingTargets.map { library.drafts[$0.path]?[tag] }) }
    private var pending: Bool { pendingValues.contains { $0 != nil } }
    private var mixedPending: Bool { pendingValues.count > 1 }
    var body: some View {
        VStack(alignment: .trailing, spacing: 4) {
            TextField(storedRow.mixed || mixedPending ? "Multiple values" : "Enter value…", text: Binding(
                get: { mixedPending ? "" : (pendingValues.first ?? nil) ?? (storedRow.mixed || current == "—" ? "" : current) },
                set: { value in
                    let displayed = mixedPending ? "" : (pendingValues.first ?? nil) ?? (storedRow.mixed || current == "—" ? "" : current)
                    if value != displayed { library.stage(tag, value: value) }
                }
            ))
            .textFieldStyle(.roundedBorder)
            .multilineTextAlignment(.leading)
            .font(.system(size: 11))
            .foregroundStyle(.primary)
            .overlay(RoundedRectangle(cornerRadius: 5).strokeBorder(
                pending ? accent.tint.opacity(0.65) : storedRow.mixed ? Color.orange.opacity(0.45) : .clear))
            .disabled(library.busy || library.exif == nil || library.editingTargets.isEmpty)
            .accessibilityLabel("Edit " + readable(row.name))
            .help("Writes " + tag + " to the selected files when you choose Save Changes.")
            if row.group == "Composite" {
                Text("Edits " + readable(tag.components(separatedBy: ":").last ?? tag))
                    .font(.system(size: 9)).foregroundStyle(.secondary)
            }
            if mixedPending {
                MetadataStatusLabel(title: "Pending edits differ", symbol: "circle.lefthalf.filled").font(.system(size: 9))
            } else if pending {
                Text("Unsaved").font(.system(size: 9)).foregroundStyle(.secondary)
            }
        }
    }
}

struct WorkflowView: View {
    @Environment(\.joongaAccent) private var accent
    @ObservedObject var library: Library
    @Environment(\.dismiss) private var dismiss
    @State private var selectedIDs = Set<String>()
    @State private var transferStatus = ""
    @State private var search = ""
    @State private var name = ""
    @State private var operations = ""
    @State private var scope = "selection"
    @State private var creating = false
    @State private var confirmingDelete = false
    @FocusState private var focusName: Bool

    private var selectedPreset: Preset? {
        selectedIDs.count == 1 ? library.presets.first { selectedIDs.contains($0.id) } : nil
    }
    private var hasEditor: Bool { creating || selectedPreset != nil }
    private var filteredPresets: [Preset] {
        library.presets.filter { search.isEmpty || ($0.name + " " + $0.category).localizedCaseInsensitiveContains(search) }
            .sorted { $0.name.localizedStandardCompare($1.name) == .orderedAscending }
    }
    private var dirty: Bool {
        guard let preset = selectedPreset else { return creating && (!name.isEmpty || !operations.isEmpty) }
        return name != preset.name || arguments != preset.operations
    }
    private var validation: String? {
        for (index, line) in operations.components(separatedBy: .newlines).enumerated() {
            let value = line.trimmingCharacters(in: .whitespaces)
            if !value.isEmpty && value.range(of: "^-[A-Za-z][A-Za-z0-9:#-]*(=|<|>)", options: .regularExpression) == nil {
                return "Line \(index + 1): use -Tag=Value or -Destination<Source."
            }
        }
        return nil
    }
    private var canSave: Bool {
        hasEditor && !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && !arguments.isEmpty && validation == nil
    }

    // Keep selection changes synchronous so a cancelled alert preserves the editor and list selection.
    private func resolveEdits() -> Bool {
        guard dirty else { return true }
        let alert = NSAlert()
        alert.messageText = "Save changes to this preset?"
        alert.informativeText = "Your changes will be lost if you don’t save them."
        alert.addButton(withTitle: "Save")
        alert.addButton(withTitle: "Cancel")
        alert.addButton(withTitle: "Don’t Save")
        switch alert.runModal() {
        case .alertFirstButtonReturn: return saveWorkflow()
        case .alertThirdButtonReturn: return true
        default: return false
        }
    }
    private func select(_ ids: Set<String>) {
        guard ids != selectedIDs, resolveEdits() else { return }
        selectedIDs = ids; creating = false; transferStatus = ""
        name = selectedPreset?.name ?? ""
        operations = selectedPreset?.operations.joined(separator: "\n") ?? ""
    }
    private func newWorkflow() {
        library.requestNewWorkflow = false
        guard resolveEdits() else { return }
        selectedIDs = []; creating = true; search = ""; name = ""; operations = ""; transferStatus = ""; focusName = true
    }
    @discardableResult private func saveWorkflow() -> Bool {
        guard canSave else {
            transferStatus = validation ?? "Give this preset a name and at least one metadata assignment."
            return false
        }
        let id = selectedPreset?.id ?? "personal-" + UUID().uuidString
        let preset = Preset(id: id, name: name.trimmingCharacters(in: .whitespacesAndNewlines),
                            category: selectedPreset?.category ?? "My Presets", operations: arguments)
        guard library.savePreset(preset) else { transferStatus = "Could not save the preset."; return false }
        selectedIDs = [id]; name = preset.name; creating = false; transferStatus = "Preset saved."
        return true
    }
    private func duplicateWorkflow() {
        guard let preset = selectedPreset, resolveEdits() else { return }
        let latest = library.presets.first { $0.id == preset.id } ?? preset
        selectedIDs = []; creating = true; name = latest.name + " Copy"
        operations = latest.operations.joined(separator: "\n"); transferStatus = ""; focusName = true
    }
    private func deleteSelection() {
        if library.deletePresets(selectedIDs) {
            selectedIDs = []; creating = false; name = ""; operations = ""; transferStatus = "Presets deleted."
        }
    }
    var targets: [URL] {
        switch scope {
        case "originals": return library.originalURLs
        case "media": return library.mediaItems.map(\.url)
        case "exports": return library.files.map(\.exported)
        default: return library.selectedTargets
        }
    }
    var arguments: [String] {
        operations.components(separatedBy: .newlines).map { $0.trimmingCharacters(in: .whitespaces) }.filter { !$0.isEmpty }
    }
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(systemName: "slider.horizontal.3").foregroundStyle(.secondary)
                Text("Presets").font(.system(size: 14, weight: .semibold))
                Spacer()
                Button("Import…") { transferStatus = library.importWorkflows() }
                Menu("Export") {
                    Button("Selected Presets (\(selectedIDs.count))…") {
                        transferStatus = library.exportWorkflows(library.presets.filter { selectedIDs.contains($0.id) })
                    }.disabled(selectedIDs.isEmpty)
                    Button("Entire Library…") { transferStatus = library.exportWorkflows(library.presets) }
                        .disabled(library.presets.isEmpty)
                }.fixedSize()
                Button("Done") { if resolveEdits() { dismiss() } }.keyboardShortcut(.cancelAction)
            }.tint(.primary).controlSize(.small).padding(.horizontal, 18).frame(height: 50).background(.ultraThinMaterial)
            Divider()
            HStack(spacing: 0) {
                VStack(spacing: 0) {
                    HStack(spacing: 6) {
                        Image(systemName: "magnifyingglass").foregroundStyle(.secondary)
                        TextField("Search presets", text: $search).textFieldStyle(.plain)
                        if !search.isEmpty {
                            Button { search = "" } label: { Image(systemName: "xmark.circle.fill") }
                                .buttonStyle(.plain).foregroundStyle(.secondary).help("Clear search")
                        }
                    }.padding(8).background(Color.primary.opacity(0.045), in: RoundedRectangle(cornerRadius: 7)).padding(12)
                    List(selection: Binding(get: { selectedIDs }, set: { select($0) })) {
                        ForEach(filteredPresets) { preset in
                            HStack(spacing: 10) {
                                Image(systemName: "slider.horizontal.3").foregroundStyle(.secondary).frame(width: 20)
                                VStack(alignment: .leading, spacing: 3) {
                                    Text(preset.name).font(.system(size: 12, weight: .medium)).lineLimit(2)
                                    Text("\(preset.operations.count) assignments").font(.system(size: 10)).foregroundStyle(.secondary)
                                }
                            }.padding(.vertical, 6).tag(preset.id)
                        }
                    }.listStyle(.sidebar)
                    .accentColor(accent.tint)
                    .tint(accent.tint)
                    .overlay { if filteredPresets.isEmpty { Text(search.isEmpty ? "Your presets appear here" : "No matching presets").font(.caption).foregroundStyle(.secondary).allowsHitTesting(false) } }
                    .onDeleteCommand { if !selectedIDs.isEmpty { confirmingDelete = true } }
                    Divider()
                    HStack(spacing: 12) {
                        Button(action: newWorkflow) { Label("New", systemImage: "plus") }.help("New Preset · ⌘N")
                        Spacer()
                        Button(action: duplicateWorkflow) { Image(systemName: "plus.square.on.square") }
                            .help("Duplicate Preset").accessibilityLabel("Duplicate Preset").disabled(selectedIDs.count != 1)
                        Button { confirmingDelete = true } label: { Image(systemName: "trash") }
                            .help("Delete Preset").accessibilityLabel("Delete Preset").disabled(selectedIDs.isEmpty)
                    }.buttonStyle(.borderless).tint(.primary).padding(12)
                }.frame(width: 230).background(Vibrancy())
                Divider()
                if hasEditor { editor.background(Vibrancy(material: .underWindowBackground, blendingMode: .behindWindow)) }
                else {
                    VStack(spacing: 14) {
                        Image(systemName: selectedIDs.count > 1 ? "square.stack" : "slider.horizontal.3")
                            .font(.system(size: 36, weight: .ultraLight)).foregroundStyle(.secondary)
                        Text(selectedIDs.count > 1 ? "\(selectedIDs.count) Presets Selected" : "Your Metadata, One Preset")
                            .font(.system(size: 17, weight: .semibold))
                        Text(selectedIDs.count > 1 ? "Export your selection to use it on another Mac,\nor select one preset to edit it." : "Save repeatable metadata edits, then apply them\nto the files you select.")
                            .font(.system(size: 12)).foregroundStyle(.secondary).multilineTextAlignment(.center)
                        if selectedIDs.isEmpty { Button("New Preset", action: newWorkflow).buttonStyle(.borderedProminent).tint(accent.tint) }
                    }
                    .padding(28)
                    .background(.thinMaterial, in: RoundedRectangle(cornerRadius: 14, style: .continuous))
                    .overlay(RoundedRectangle(cornerRadius: 14, style: .continuous).strokeBorder(Color.primary.opacity(0.08)))
                    .padding(24)
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
                    .background(Vibrancy(material: .underWindowBackground, blendingMode: .behindWindow))
                }
            }
            Divider()
            HStack(spacing: 6) {
                Image(systemName: transferStatus.isEmpty ? "internaldrive" : "info.circle")
                Text(transferStatus.isEmpty ? "Saved on this Mac · Import and export to share presets" : transferStatus).lineLimit(2)
                Spacer()
            }.font(.system(size: 10)).foregroundStyle(.secondary).padding(.horizontal, 16).frame(height: 34).background(.ultraThinMaterial)
        }.frame(width: 800, height: 610)
        .interactiveDismissDisabled(dirty)
        .confirmationDialog("Delete \(selectedIDs.count) presets?", isPresented: $confirmingDelete) {
            Button("Delete Presets", role: .destructive, action: deleteSelection)
            Button("Cancel", role: .cancel) {}
        } message: { Text("Export a copy first if you want to keep these presets.") }
        .onAppear { if library.requestNewWorkflow { newWorkflow() } }
        .onChange(of: library.requestNewWorkflow) { if $0 { newWorkflow() } }
        .onChange(of: library.saveWorkflowRequest) { _ in _ = saveWorkflow() }
    }
    private var editor: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                Text(creating ? "NEW PRESET" : "EDIT PRESET").font(.system(size: 10, weight: .semibold)).foregroundStyle(.secondary)
                Spacer()
                if dirty { Text("Unsaved changes").font(.system(size: 10)).foregroundStyle(.secondary) }
            }
            TextField("Preset name", text: $name).font(.system(size: 18, weight: .semibold))
                .textFieldStyle(.roundedBorder).focused($focusName)
            VStack(alignment: .leading, spacing: 0) {
                HStack {
                    Text("Metadata assignments").font(.system(size: 11, weight: .medium))
                    Spacer()
                    Text("\(arguments.count) \(arguments.count == 1 ? "tag" : "tags")").font(.system(size: 10)).foregroundStyle(.secondary)
                }.padding(10)
                Divider()
                ZStack(alignment: .topLeading) {
                    TextEditor(text: $operations).font(.system(size: 12, design: .monospaced))
                        .scrollContentBackground(.hidden).padding(8)
                        .accessibilityLabel("Metadata assignments")
                    if operations.isEmpty {
                        Text("-Make=Camera maker\n-Model=Camera model")
                            .font(.system(size: 12, design: .monospaced)).foregroundStyle(.tertiary)
                            .padding(.horizontal, 13).padding(.vertical, 10).allowsHitTesting(false)
                    }
                }
            }.background(.thinMaterial, in: RoundedRectangle(cornerRadius: 8))
                .overlay(RoundedRectangle(cornerRadius: 8).strokeBorder(Color.primary.opacity(0.12)))
                .frame(minHeight: 180)
            if let validation = validation {
                MetadataStatusLabel(title: validation, symbol: "exclamationmark.circle").font(.caption)
            } else {
                Text("One assignment per line. Values can contain spaces; quotes aren’t needed.").font(.system(size: 10)).foregroundStyle(.secondary)
            }
            HStack {
                Text("Stage for").font(.system(size: 12))
                Picker("Stage for", selection: $scope) {
                    Text("Selected files (\(library.selectedTargets.count))").tag("selection")
                    if library.mode == .transfer {
                        Text("All exports (\(library.files.count))").tag("exports")
                        Text("All originals (\(library.originalURLs.count))").tag("originals")
                    } else { Text("All loaded files (\(library.mediaItems.count))").tag("media") }
                }.labelsHidden()
            }
            if targets.isEmpty {
                Label("Select files in either pane before applying a preset.", systemImage: "cursorarrow.click")
                    .font(.system(size: 11)).foregroundStyle(.secondary)
            } else {
                DisclosureGroup("Review \(targets.count) target \(targets.count == 1 ? "file" : "files")") {
                    ScrollView { VStack(alignment: .leading, spacing: 5) {
                        ForEach(targets, id: \.path) { Text($0.path).font(.caption).textSelection(.enabled) }
                    }.frame(maxWidth: .infinity, alignment: .leading) }.frame(maxHeight: 70)
                }.font(.system(size: 11))
            }
            Divider()
            HStack {
                Button("Save Preset") { _ = saveWorkflow() }.disabled(!canSave || !dirty).help("Save Preset · ⌘S")
                Spacer()
                Button(targets.count == 1 ? "Stage for 1 File" : "Stage for \(targets.count) Files") {
                    let args = arguments, urls = targets
                    // Applying a draft does not silently throw away unsaved workflow changes.
                    guard resolveEdits() else { return }
                    dismiss(); library.writeMetadata(args, to: urls)
                }.buttonStyle(.borderedProminent).tint(accent.tint)
                    .disabled(library.busy || library.exif == nil || targets.isEmpty || arguments.isEmpty || validation != nil)
            }
        }.padding(22).frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
