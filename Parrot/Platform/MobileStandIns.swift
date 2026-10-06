#if os(iOS)
import Foundation

// iPhone and iPad stand-ins for Mac services that have no iOS equivalent.
// Each keeps the Mac type's API, so shared code compiles unchanged and the
// feature simply stays off.

/// The App Store updates iOS apps; there is no in-app updater.
@MainActor
final class AppUpdater {
    static let shared = AppUpdater()
    private init() {}

    /// Set by RecordingManager on the Mac to hold an update during a call.
    var isBusy: () -> Bool = { false }

    func checkForUpdates() {}
    func becameIdle() {}
    func restartNow() {}
    var isAvailable: Bool { false }
    var automaticallyUpdates: Bool {
        get { false }
        set {}
    }

    nonisolated static var currentVersion: String {
        Bundle.main.infoDictionary?["CFBundleShortVersionString"] as? String ?? "dev"
    }
}

/// iOS apps can't hold a system-wide keyboard shortcut. Mark and Mute stay
/// on screen in the live call view (and work from a hardware keyboard while
/// Parrot is in front).
final class GlobalHotKey {
    struct Combo: Equatable {
        let keyCode: UInt32
        let modifiers: UInt32
        let display: String

        static let markMoment = Combo(keyCode: 0, modifiers: 0, display: "⌃⌥M")
        static let muteMe = Combo(keyCode: 0, modifiers: 0, display: "⌃⌥⇧M")
    }

    var isRegistered: Bool { false }

    @discardableResult
    func register(_ combo: Combo, action: @escaping () -> Void) -> Bool { false }

    func unregister() {}
}
#endif
