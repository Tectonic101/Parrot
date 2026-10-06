#if os(macOS)
import AppKit
import Foundation

/// One-click connect. Parrot is sandboxed, so it can't edit another app's
/// config; each AI app gets the cleanest path it supports instead:
/// Claude Desktop an install file (`.mcpb`, the same one the release ships),
/// Cursor its install link, Claude Code and Codex a command to paste.
enum MCPBundle {

    static let bundleID = "com.uygar.parrot"
    static let claudeBundleID = "com.anthropic.claudefordesktop"
    static let cursorBundleID = "com.todesktop.230313mzl4w4u92"
    /// Codex now ships inside the ChatGPT app, which carries this id.
    static let codexBundleID = "com.openai.codex"
    static let codexCLIPath = "Contents/Resources/codex-cli/bin/codex"
    static let claudeDownloadURL = URL(string: "https://claude.ai/download")!

    // MARK: Commands and links

    /// Single-quoted for the shell: spaces and quotes in the path survive a paste.
    static func shellQuoted(_ s: String) -> String {
        "'" + s.replacingOccurrences(of: "'", with: "'\\''") + "'"
    }

    /// For every project, not just the folder it's pasted in.
    static func claudeCodeCommand(executable: String) -> String {
        "claude mcp add --scope user parrot -- \(shellQuoted(executable)) --mcp"
    }

    /// `codexCLI`: the codex tool inside the ChatGPT app, when found. Most
    /// people never put `codex` on their PATH, so a bare "codex" would fail.
    static func codexCommand(executable: String, codexCLI: String? = nil) -> String {
        "\(codexCLI.map(shellQuoted) ?? "codex") mcp add parrot -- \(shellQuoted(executable)) --mcp"
    }

    /// The codex tool inside the installed ChatGPT app, if it's there.
    static var installedCodexCLI: String? {
        guard let app = NSWorkspace.shared.urlForApplication(withBundleIdentifier: codexBundleID) else { return nil }
        let cli = app.appendingPathComponent(codexCLIPath).path
        return FileManager.default.isExecutableFile(atPath: cli) ? cli : nil
    }

    /// Cursor's install link: base64 of the server's JSON, percent-encoded so
    /// "+" doesn't turn into a space on the way in.
    static func cursorLink(executable: String) -> URL? {
        let config: [String: Any] = ["command": executable, "args": ["--mcp"]]
        guard let json = try? JSONSerialization.data(withJSONObject: config, options: [.sortedKeys, .withoutEscapingSlashes]),
              let encoded = json.base64EncodedString().addingPercentEncoding(withAllowedCharacters: .alphanumerics)
        else { return nil }
        return URL(string: "cursor://anysphere.cursor-deeplink/mcp/install?name=parrot&config=\(encoded)")
    }

    // MARK: The Claude Desktop install file

    /// Starts Parrot's server: where the app was when this was made, the
    /// usual places, then wherever Spotlight finds it by its app id, so a
    /// moved app still connects. `appPath` nil for the release file.
    static func launcher(appPath: String?) -> String {
        let places = ([appPath].compactMap { $0 } + ["/Applications/Parrot.app"]).map(shellQuoted)
            + ["\"$HOME/Applications/Parrot.app\""]
        return """
        #!/bin/sh
        # Starts Parrot's read-only meeting server for Claude. No network, no writes.
        for app in \(places.joined(separator: " ")) \\
            "$(mdfind "kMDItemCFBundleIdentifier == '\(bundleID)'" 2>/dev/null | head -n 1)"; do
          if [ -x "$app/Contents/MacOS/Parrot" ]; then exec "$app/Contents/MacOS/Parrot" --mcp; fi
        done
        echo "Parrot isn't installed. Get it at https://openparrot.app" >&2
        exit 1

        """
    }

    static func manifest(version: String) -> [String: Any] {
        [
            "manifest_version": "0.3",
            "name": "parrot",
            "display_name": "Parrot",
            "version": version,
            "description": "Your recorded meetings in Claude: search them, catch up, list promises, draft follow-ups. Local and read-only.",
            "long_description": """
            Parrot records your calls on your Mac and writes the transcript with real speaker names. \
            This connects Claude to those meetings, read-only.

            - **Find anything:** "When did Sarah mention the budget?"
            - **Catch up:** "Everything with Acme this quarter"
            - **Never drop a promise:** "What did I promise last week?"
            - **Write it for me:** follow-up emails, Slack updates, meeting notes
            - **Second opinion:** "Coach me across my last 10 calls"
            - **Prepare:** "Brief me for my call with Acme"

            Claude can't change, delete or record anything. You choose what it sees in Parrot, and \
            meetings marked on-device only are never shown. When Claude reads a meeting, that text \
            goes to Anthropic under your Claude account.
            """,
            "author": ["name": "Parrot", "url": "https://openparrot.app"],
            "homepage": "https://openparrot.app",
            "documentation": "https://openparrot.app/help/connections.html",
            "support": "https://github.com/turantekin/Parrot/issues",
            "repository": ["type": "git", "url": "https://github.com/turantekin/Parrot"],
            "license": "GPL-3.0",
            "icon": "icon.png",
            "keywords": ["meetings", "transcripts", "calls", "notes", "local", "mac"],
            "privacy_policies": ["https://openparrot.app/privacy"],
            "compatibility": ["platforms": ["darwin"]],
            "server": [
                "type": "binary",
                "entry_point": "server/launch.sh",
                "mcp_config": ["command": "/bin/sh", "args": ["${__dirname}/server/launch.sh"]],
            ],
            "tools": MCPServer.tools.map { ["name": $0["name"] ?? "", "description": $0["description"] ?? ""] },
            "prompts": MCPPrompts.all.map { p in
                [
                    "name": p.name,
                    "description": p.description,
                    "arguments": p.arguments.map(\.name),
                    "text": p.text(Dictionary(uniqueKeysWithValues: p.arguments.map { ($0.name, "${arguments.\($0.name)}") })),
                ] as [String: Any]
            },
        ]
    }

    /// Builds `Parrot.mcpb` (manifest, launcher, icon) in the temp folder.
    static func build(appPath: String?, version: String, icon: NSImage?) throws -> URL {
        let fm = FileManager.default
        let root = fm.temporaryDirectory.appendingPathComponent("parrot-mcpb-\(UUID().uuidString)", isDirectory: true)
        defer { try? fm.removeItem(at: root) }
        try fm.createDirectory(at: root.appendingPathComponent("server"), withIntermediateDirectories: true)
        let manifest = try JSONSerialization.data(withJSONObject: manifest(version: version),
                                                  options: [.prettyPrinted, .sortedKeys, .withoutEscapingSlashes])
        try manifest.write(to: root.appendingPathComponent("manifest.json"))
        let script = root.appendingPathComponent("server/launch.sh")
        try launcher(appPath: appPath).write(to: script, atomically: true, encoding: .utf8)
        try fm.setAttributes([.posixPermissions: 0o755], ofItemAtPath: script.path)
        if let png = icon.flatMap(pngData) { try png.write(to: root.appendingPathComponent("icon.png")) }

        let out = fm.temporaryDirectory.appendingPathComponent("Parrot.mcpb")
        try? fm.removeItem(at: out)
        // The manifest must sit at the zip's root: no parent folder, no resource forks.
        let zip = Process()
        zip.executableURL = URL(fileURLWithPath: "/usr/bin/ditto")
        zip.arguments = ["-c", "-k", "--norsrc", root.path, out.path]
        try zip.run()
        zip.waitUntilExit()
        guard zip.terminationStatus == 0 else { throw CocoaError(.fileWriteUnknown) }
        return out
    }

    /// `Parrot --mcpb <out> <version> [icon]`, run by scripts/release.sh on
    /// the plain release binary: the install file for the GitHub release. Its
    /// launcher has no app path baked in; it finds Parrot where it's installed.
    static func writeRelease(to path: String, version: String, iconPath: String?) -> Int32 {
        let icon = iconPath.flatMap(NSImage.init(contentsOfFile:)) ?? bundledIcon
        do {
            let built = try build(appPath: nil, version: version, icon: icon)
            let out = URL(fileURLWithPath: path)
            try? FileManager.default.removeItem(at: out)
            try FileManager.default.moveItem(at: built, to: out)
            print("mcpb: wrote \(out.path)\(icon == nil ? " (no icon)" : "")")
            return 0
        } catch {
            FileHandle.standardError.write(Data("mcpb: \(error.localizedDescription)\n".utf8))
            return 1
        }
    }

    /// The icon file inside the app. Not `NSApp.applicationIconImage`: macOS
    /// can hand that back with a badge drawn on it (a "not allowed" sign on a
    /// build run from outside Applications), and Claude shows it as the logo.
    static var bundledIcon: NSImage? {
        Bundle.main.url(forResource: "AppIcon", withExtension: "icns").flatMap(NSImage.init(contentsOf:))
    }

    /// 512 px PNG of the app icon.
    private static func pngData(_ image: NSImage) -> Data? {
        let size = NSSize(width: 512, height: 512)
        guard let rep = NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 512, pixelsHigh: 512, bitsPerSample: 8,
                                         samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB,
                                         bytesPerRow: 0, bitsPerPixel: 0) else { return nil }
        rep.size = size
        NSGraphicsContext.saveGraphicsState()
        NSGraphicsContext.current = NSGraphicsContext(bitmapImageRep: rep)
        image.draw(in: NSRect(origin: .zero, size: size))
        NSGraphicsContext.restoreGraphicsState()
        return rep.representation(using: .png, properties: [:])
    }
}
#endif
