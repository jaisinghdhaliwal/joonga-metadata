import AppKit
import SwiftUI

extension Library {
    var workflowDirectory: URL { workflowFile.deletingLastPathComponent() }

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
    @AppStorage("ShowPreviewOnLaunch") private var showPreviewOnLaunch = true

    var body: some View {
        VStack(alignment: .leading, spacing: 0) {
            Label("Settings", systemImage: "gearshape")
                .font(.title3.weight(.semibold))
                .padding(20)
                .accessibilityAddTraits(.isHeader)
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
                        Text(library.exif ?? "Not found")
                            .foregroundStyle(.secondary)
                            .textSelection(.enabled)
                    }
                    LabeledContent("Workflows") {
                        Button("Open Library Folder") { library.openWorkflowDirectory() }
                    }
                }
            }
            .formStyle(.grouped)
            Divider()
            HStack {
                Spacer()
                Button("Done") { dismiss() }.keyboardShortcut(.defaultAction)
            }.padding(16)
        }
        .frame(width: 590, height: 420)
        .onChange(of: showPreviewOnLaunch) { library.showInspector = $0 }
    }
}
