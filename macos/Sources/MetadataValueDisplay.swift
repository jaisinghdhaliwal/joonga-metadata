import SwiftUI

struct MetadataValueDisplay: View {
    let row: MetadataRow
    let preferOriginal: Bool
    let hasOriginal: Bool

    var primary: String {
        preferOriginal && hasOriginal ? row.original : row.exported
    }

    var comparison: String {
        preferOriginal ? row.exported : row.original
    }

    var body: some View {
        VStack(alignment: .trailing, spacing: 4) {
            Text(primary == "—" ? "Not set" : primary)
                .fontWeight(.medium)
                .foregroundStyle(primary == "—" ? Color.secondary : Color.primary)
            if hasOriginal && row.changed {
                Text((preferOriginal ? "Export: " : "Original: ") + (comparison == "—" ? "Not set" : comparison))
                    .foregroundStyle(.secondary)
                    .font(.system(size: 10))
            }
        }
    }
}
