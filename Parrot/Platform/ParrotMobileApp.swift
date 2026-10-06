#if os(iOS)
import SwiftUI
import SwiftData

/// iPhone and iPad entry point: the same window content as the Mac app
/// (ContentView, onboarding, Settings in the main pane), minus the Mac's
/// menu bar extra, Settings window and command-line harnesses.
@main
struct ParrotMobileApp: App {
    @State private var recordingManager = RecordingManager()
    @State private var appSession = AppSession()
    @AppStorage("hasCompletedOnboarding") private var hasCompletedOnboarding = false
    @AppStorage("appearance") private var appearance = Appearance.system

    private var showOnboarding: Binding<Bool> {
        Binding(get: { !hasCompletedOnboarding }, set: { hasCompletedOnboarding = !$0 })
    }

    let sharedModelContainer: ModelContainer = {
        let schema = Schema([Meeting.self, TranscriptSegment.self, CallInsight.self, CallProfile.self, SpeakerProfile.self])
        let configuration = ModelConfiguration(schema: schema, isStoredInMemoryOnly: false)
        do {
            return try ModelContainer(for: schema, configurations: [configuration])
        } catch {
            fatalError("Could not create ModelContainer: \(error)")
        }
    }()

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(recordingManager)
                .environment(recordingManager.profileStore)
                .environment(appSession)
                .sheet(isPresented: showOnboarding) {
                    OnboardingView(isPresented: showOnboarding)
                        .environment(recordingManager)
                        .interactiveDismissDisabled()
                }
                .preferredColorScheme(colorScheme)
                // openparrot:// links (an AI app citing a moment).
                .onOpenURL { url in
                    guard let link = ParrotLink.parse(url) else { return }
                    appSession.pendingJump = AppSession.Jump(meetingID: link.id, time: link.time)
                }
                // A recording keeps running in the background (audio
                // background mode); the screen stays awake while it's in front.
                .onChange(of: recordingManager.isRecording, initial: true) { _, recording in
                    UIApplication.shared.isIdleTimerDisabled = recording
                }
        }
        .modelContainer(sharedModelContainer)
    }

    private var colorScheme: ColorScheme? {
        switch appearance {
        case .system: nil
        case .light: .light
        case .dark: .dark
        }
    }
}
#endif
