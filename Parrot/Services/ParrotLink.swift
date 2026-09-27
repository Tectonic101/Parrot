import Foundation

/// `openparrot://meeting/<id>?t=<seconds>`: opens a meeting in Parrot, at a
/// moment when `t` is given. AI apps get these in tool results, so a time
/// they cite opens the call right there, like Ask Parrot's chips. Opening a
/// page is all a link can do. "openparrot", not "parrot": other Mac apps are
/// called Parrot too, and a shared scheme would go to whichever macOS picks.
enum ParrotLink {

    static let scheme = "openparrot"

    static func meeting(_ id: UUID, at time: TimeInterval? = nil) -> String {
        "\(scheme)://meeting/\(id.uuidString)" + (time.map { "?t=\(Int(max(0, $0)))" } ?? "")
    }

    /// The meeting and moment a link points at, or nil for anything else.
    static func parse(_ url: URL) -> (id: UUID, time: TimeInterval?)? {
        guard url.scheme?.lowercased() == scheme, url.host?.lowercased() == "meeting",
              let id = UUID(uuidString: url.lastPathComponent) else { return nil }
        let t = URLComponents(url: url, resolvingAgainstBaseURL: false)?
            .queryItems?.first { $0.name == "t" }?.value.flatMap(Double.init)
        return (id, t.flatMap { $0.isFinite && $0 >= 0 ? $0 : nil })
    }
}
