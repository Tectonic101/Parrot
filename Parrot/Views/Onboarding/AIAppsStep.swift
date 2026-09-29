import AppKit
import SwiftUI

/// Claude, Cursor and Codex, just before Ready. Honest up front: this is the
/// one part of Parrot where what's read goes to another company. Never shown
/// on the Private path (see OnboardingFlow.steps). Continue skips it.
struct AIAppsStep: View {
    @AppStorage(MCPServer.enabledKey) private var enabled = false
    @State private var connected = false
    @State private var problem: String?

    private static let apps: [(name: String, bundleID: String)] = [
        ("Claude", MCPBundle.claudeBundleID),
        ("Cursor", MCPBundle.cursorBundleID),
        ("Codex", MCPBundle.codexBundleID),
    ]

    var body: some View {
        let claude = AIApps.appURL(MCPBundle.claudeBundleID)
        VStack(spacing: 18) {
            Spacer()
            StepHeader(title: "Use your meetings in Claude",
                       subtitle: "Ask Claude, Cursor or Codex about your calls. Your own plan does the thinking.")

            HStack(spacing: 10) {
                ForEach(Self.apps, id: \.name) { app in appChip(app.name, bundleID: app.bundleID) }
            }

            VStack(alignment: .leading, spacing: 6) {
                ForEach([AIApps.firstQuestion, "Brief me for my call with Acme."], id: \.self) { question in
                    Text("“\(question)”")
                        .font(Theme.Typography.body)
                        .foregroundStyle(Theme.Colors.ink2)
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)

            HStack(alignment: .top, spacing: 10) {
                Image(systemName: "info.circle")
                    .foregroundStyle(Theme.Colors.ink2)
                VStack(alignment: .leading, spacing: 4) {
                    Text("Not private like the rest of Parrot")
                        .font(Theme.Typography.cardTitle)
                    Text("When you ask, what the app reads goes to its company (Anthropic, OpenAI or Cursor's AI provider) under your account. It stays off until you connect, and never sees audio, API keys or on-device-only meetings.")
                        .font(Theme.Typography.secondary)
                        .foregroundStyle(Theme.Colors.ink2)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 0)
            }
            .padding(Theme.Metrics.popoverPad)
            .background(Theme.Colors.chip.opacity(0.5), in: RoundedRectangle(cornerRadius: Theme.Metrics.cardRadius))

            VStack(spacing: 8) {
                if let claude {
                    Button(connected ? "Opened Claude. Click Install there." : "Connect Claude") { connect(claude) }
                        .buttonStyle(.borderedProminent)
                        .disabled(connected)
                } else {
                    Button("Get Claude") { NSWorkspace.shared.open(MCPBundle.claudeDownloadURL) }
                }
                Text(problem ?? "Cursor, Codex and Claude Code connect from Claude & AI Apps in the sidebar, any time. Continue skips this.")
                    .font(Theme.Typography.caption)
                    .foregroundStyle(problem == nil ? Theme.Colors.ink3 : Theme.Colors.warn)
                    .multilineTextAlignment(.center)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
        }
        .frame(maxWidth: 480)
        .padding(Theme.Metrics.pad)
    }

    /// The app's own icon when it's installed, a plain one when it isn't.
    private func appChip(_ name: String, bundleID: String) -> some View {
        HStack(spacing: 6) {
            if let url = AIApps.appURL(bundleID) {
                Image(nsImage: NSWorkspace.shared.icon(forFile: url.path))
                    .resizable()
                    .frame(width: 18, height: 18)
            } else {
                Image(systemName: "app")
                    .foregroundStyle(Theme.Colors.ink3)
            }
            Text(name)
                .font(Theme.Typography.cardTitle)
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .overlay(RoundedRectangle(cornerRadius: Theme.Metrics.cardRadius).strokeBorder(Theme.Colors.line))
    }

    /// Clicking Connect is the consent: it turns on "Allow AI apps to read my
    /// meetings", then hands Claude its install file.
    private func connect(_ claude: URL) {
        enabled = true
        do {
            try AIApps.connectClaude(at: claude)
            connected = true
            problem = nil
        } catch {
            problem = "Couldn't make the install file. Try again from Claude & AI Apps in the sidebar."
        }
    }
}
