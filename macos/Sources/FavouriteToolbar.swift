import SwiftUI

struct FavouriteToolbar: View {
    @ObservedObject var library: Library
    let save: () -> Void
    @State private var showing = false

    var body: some View {
        HStack(spacing: 2) {
            Button(action: save) { Image(systemName: "star") }
                .help("Save current location")
                .accessibilityLabel("Save current location")
                .disabled(library.busy)
            Button { showing.toggle() } label: { Image(systemName: "chevron.down") }
                .help("Show favourites")
                .accessibilityLabel("Show favourites")
                .popover(isPresented: $showing, arrowEdge: .bottom) {
                    VStack(alignment: .leading, spacing: 6) {
                        Button("Save Current Location…") { showing = false; save() }
                            .buttonStyle(FavouriteHighlight())
                            .disabled(library.busy || (library.lastMapPoint == nil && library.selectedMapLocation == nil))
                        Divider()
                        if library.favouriteLocations.isEmpty {
                            Text("No saved favourites").foregroundStyle(.secondary).padding(6)
                        } else {
                            ScrollView {
                                VStack(spacing: 2) {
                                    ForEach(library.favouriteLocations) { favourite in
                                        HStack(spacing: 4) {
                                            Button(favourite.name) {
                                                library.focusMapPoint(favourite.point)
                                                showing = false
                                            }.buttonStyle(FavouriteHighlight())
                                            Button {
                                                library.stageLocation(favourite.point, targets: library.selectedTargets)
                                                library.focusMapPoint(favourite.point)
                                                showing = false
                                            } label: { Image(systemName: "mappin") }
                                            .help("Apply to selection")
                                            .accessibilityLabel("Apply \(favourite.name) to selection")
                                            .disabled(library.busy || library.exif == nil || library.selectedTargets.isEmpty)
                                            Button {
                                                library.removeFavouriteLocation(id: favourite.id)
                                            } label: { Image(systemName: "trash") }
                                            .help("Remove favourite")
                                            .accessibilityLabel("Remove \(favourite.name)")
                                        }
                                    }
                                }
                            }.frame(height: CGFloat(min(7, library.favouriteLocations.count)) * 32)
                        }
                    }.frame(width: 260).padding(10)
                }
        }.buttonStyle(.borderless)
    }
}

private struct FavouriteHighlight: ButtonStyle {
    @Environment(\.joongaAccent) private var accent
    @Environment(\.isEnabled) private var enabled
    @State private var hovered = false

    func makeBody(configuration: Configuration) -> some View {
        let highlighted = enabled && (hovered || configuration.isPressed)
        configuration.label
            .frame(maxWidth: .infinity, alignment: .leading)
            .padding(6)
            .foregroundStyle(highlighted ? accent.selectionText(active: true) : enabled ? Color.primary : .secondary)
            .background(highlighted ? accent.selectionFill(active: true) : .clear, in: RoundedRectangle(cornerRadius: 5))
            .contentShape(Rectangle())
            .onHover { hovered = $0 }
    }
}
