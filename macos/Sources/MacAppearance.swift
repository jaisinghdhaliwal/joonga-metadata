import AppKit
import SwiftUI

struct MacAccent {
    // System Settings removes this global key for Multicolour.
    // Graphite (-1) and explicit Blue are both overrides.
    let usesBrandColor: Bool

    init(preference: Int? = (CFPreferencesCopyAppValue("AppleAccentColor" as CFString,
                                                 kCFPreferencesAnyApplication) as? NSNumber)?.intValue) {
        usesBrandColor = preference == nil || preference == -2
    }

    var tint: Color { Color(nsColor: usesBrandColor ? Self.coral : .controlAccentColor) }
    private static let coral = NSColor(srgbRed: 249 / 255, green: 66 / 255, blue: 58 / 255, alpha: 1)
    private static let ink = NSColor(srgbRed: 19 / 255, green: 30 / 255, blue: 41 / 255, alpha: 1)

    func selectionFill(active: Bool) -> Color {
        Color(nsColor: selectionBackground(active: active))
    }

    func selectionText(active: Bool) -> Color {
        Color(nsColor: selectionForeground(active: active))
    }

    func selectionBackground(active: Bool) -> NSColor {
        if !active { return .unemphasizedSelectedContentBackgroundColor }
        return usesBrandColor ? Self.coral : .selectedContentBackgroundColor
    }

    func selectionForeground(active: Bool) -> NSColor {
        if !active { return .unemphasizedSelectedTextColor }
        return usesBrandColor ? Self.ink : .alternateSelectedControlTextColor
    }
}

private struct JoongaAccentKey: EnvironmentKey {
    static let defaultValue = MacAccent()
}

extension EnvironmentValues {
    var joongaAccent: MacAccent {
        get { self[JoongaAccentKey.self] }
        set { self[JoongaAccentKey.self] = newValue }
    }
}

// Keep the tint current when Appearance settings change or the user returns to the app.
struct MacAccentModifier: ViewModifier {
    @State private var accent = MacAccent()

    func body(content: Content) -> some View {
        content
            .environment(\.joongaAccent, accent)
            .accentColor(accent.tint)
            .onReceive(NotificationCenter.default.publisher(for: NSColor.systemColorsDidChangeNotification)) { _ in
                accent = MacAccent()
            }
            .onReceive(NotificationCenter.default.publisher(for: NSApplication.didBecomeActiveNotification)) { _ in
                accent = MacAccent()
            }
    }
}

struct MetadataStatusLabel: View {
    let title: String
    let symbol: String
    var color: Color = .orange

    var body: some View {
        Label {
            Text(title).foregroundStyle(.secondary)
        } icon: {
            Image(systemName: symbol).foregroundStyle(color)
        }
    }
}
