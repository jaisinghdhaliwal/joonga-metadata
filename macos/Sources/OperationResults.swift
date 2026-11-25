import AppKit
import SwiftUI

/// Long batch logs stay readable and selectable instead of expanding a system alert.
struct OperationResults: View {
    @ObservedObject var library: Library
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                Image(systemName: "doc.text.magnifyingglass").foregroundStyle(.secondary)
                VStack(alignment: .leading, spacing: 3) {
                    Text("Operation Results").font(.system(size: 14, weight: .semibold))
                    Text(library.message.isEmpty ? "Review details for each file" : library.message)
                        .font(.caption).foregroundStyle(.secondary)
                }
                Spacer()
                Button("Done") { library.report = nil }.keyboardShortcut(.defaultAction)
            }.padding(18).background(.ultraThinMaterial)
            Divider()
            ScrollView {
                Text(library.report ?? "").font(.system(size: 11, design: .monospaced))
                    .textSelection(.enabled).fixedSize(horizontal: false, vertical: true)
                    .frame(maxWidth: .infinity, alignment: .topLeading).padding(18)
            }.background(Color(nsColor: .textBackgroundColor).opacity(0.7))
            Divider()
            HStack {
                Button("Copy Results") {
                    NSPasteboard.general.clearContents()
                    NSPasteboard.general.setString(library.report ?? "", forType: .string)
                }
                Spacer()
                Text("Original files are backed up by ExifTool.").font(.caption).foregroundStyle(.secondary)
            }.padding(14).background(.ultraThinMaterial)
        }.frame(width: 720, height: 480).background(Vibrancy(material: .underWindowBackground, blendingMode: .behindWindow))
    }
}
