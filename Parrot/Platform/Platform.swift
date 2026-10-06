import SwiftUI
#if os(macOS)
import AppKit
typealias PlatformImage = NSImage
#else
import UIKit
typealias PlatformImage = UIImage
#endif

/// The handful of things Parrot asks of the OS that AppKit and UIKit spell
/// differently. Shared code calls these; each platform keeps its native
/// behavior (on the Mac they are exactly the AppKit calls they replace).
enum Platform {

    /// True on iPhone and iPad, where only the device mic can be recorded.
    static var isMobile: Bool {
        #if os(iOS)
        true
        #else
        false
        #endif
    }

    /// Opens a URL in its default handler (browser, Mail, Settings).
    static func open(_ url: URL) {
        #if os(macOS)
        NSWorkspace.shared.open(url)
        #else
        Task { @MainActor in UIApplication.shared.open(url) }
        #endif
    }

    /// Replaces the clipboard with plain text.
    static func copy(_ text: String) {
        #if os(macOS)
        NSPasteboard.general.clearContents()
        NSPasteboard.general.setString(text, forType: .string)
        #else
        UIPasteboard.general.string = text
        #endif
    }

    /// Replaces the clipboard with an image.
    static func copy(image: PlatformImage) {
        #if os(macOS)
        NSPasteboard.general.clearContents()
        NSPasteboard.general.writeObjects([image])
        #else
        UIPasteboard.general.image = image
        #endif
    }

    /// Posted when the app comes back to the front, so views can re-read
    /// permissions and settings the user may have changed elsewhere.
    static var didBecomeActiveNotification: Notification.Name {
        #if os(macOS)
        NSApplication.didBecomeActiveNotification
        #else
        UIApplication.didBecomeActiveNotification
        #endif
    }

    /// The signed-in user's full name, or "" where the OS doesn't say.
    static var userFullName: String {
        #if os(macOS)
        NSFullUserName()
        #else
        ""
        #endif
    }

    /// Shows a file in Finder. iOS has no file viewer to point at, so the
    /// Files app's "On My iPad → Parrot" folder is the user's way in.
    static func reveal(_ url: URL) {
        #if os(macOS)
        NSWorkspace.shared.activateFileViewerSelecting([url])
        #endif
    }

    /// Opens the system's privacy pane for a permission (Mac pane anchors such
    /// as "Privacy_Microphone"); on iOS, Parrot's own page in Settings.
    static func openPrivacySettings(pane: String) {
        #if os(macOS)
        open(URL(string: "x-apple.systempreferences:com.apple.preference.security?\(pane)")!)
        #else
        Task { @MainActor in
            if let url = URL(string: UIApplication.openSettingsURLString) { UIApplication.shared.open(url) }
        }
        #endif
    }

    /// The app icon, for avatars.
    @MainActor static var appIcon: PlatformImage {
        #if os(macOS)
        NSApp.applicationIconImage
        #else
        UIImage(named: "AppIcon") ?? UIImage(systemName: "bird") ?? UIImage()
        #endif
    }
}

extension Image {
    init(platformImage: PlatformImage) {
        #if os(macOS)
        self.init(nsImage: platformImage)
        #else
        self.init(uiImage: platformImage)
        #endif
    }
}

// MARK: - Styles AppKit has and UIKit doesn't

extension View {
    /// A text-link button (macOS `.link`); plain accent-tinted text on iOS.
    @ViewBuilder func linkButtonStyle() -> some View {
        #if os(macOS)
        buttonStyle(.link)
        #else
        buttonStyle(.borderless)
        #endif
    }

    /// A menu drawn as its bare label (macOS `.borderlessButton`).
    @ViewBuilder func borderlessMenuStyle() -> some View {
        #if os(macOS)
        menuStyle(.borderlessButton)
        #else
        menuStyle(.button).buttonStyle(.borderless)
        #endif
    }

    /// Radio buttons on the Mac; an inline list of choices on iOS.
    @ViewBuilder func radioGroupPickerStyle() -> some View {
        #if os(macOS)
        pickerStyle(.radioGroup)
        #else
        pickerStyle(.inline)
        #endif
    }

    /// Escape key handling (macOS only; iOS has no Escape-to-cancel).
    @ViewBuilder func onEscape(_ action: @escaping () -> Void) -> some View {
        #if os(macOS)
        onExitCommand(perform: action)
        #else
        self
        #endif
    }
}

/// A resizable side-by-side split on the Mac (`HSplitView`). On iPad it is a
/// plain side-by-side row; on iPhone, where two columns don't fit, a stack.
struct AdaptiveSplit<Content: View>: View {
    @ViewBuilder var content: () -> Content
    #if os(iOS)
    @Environment(\.horizontalSizeClass) private var sizeClass
    #endif

    var body: some View {
        #if os(macOS)
        HSplitView(content: content)
        #else
        if sizeClass == .compact {
            VStack(spacing: 0, content: content)
        } else {
            HStack(spacing: 0, content: content)
        }
        #endif
    }
}

#if os(iOS)
extension Notification.Name {
    /// iOS has no Settings window: ContentView shows Settings in its main pane.
    static let parrotShowSettings = Notification.Name("parrotShowSettings")
}

/// iOS stand-in for SwiftUI's macOS-only `openSettings` action.
struct SettingsOpener {
    func callAsFunction() {
        NotificationCenter.default.post(name: .parrotShowSettings, object: nil)
    }
}
#endif
