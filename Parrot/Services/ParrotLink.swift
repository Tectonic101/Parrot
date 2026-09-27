import Foundation

/// Links that open a meeting in Parrot, at a moment when `t` is given, like
/// Ask Parrot's chips. Opening a page is all a link can do.
///
/// Two forms. `openparrot://meeting/<id>?t=<seconds>` is what Parrot itself
/// opens ("openparrot", not "parrot": other Mac apps share that name). AI
/// apps get `https://openparrot.app/open#m=<id>&t=<seconds>` instead:
/// Claude Desktop drops links that aren't web links, and Cursor won't follow
/// them. That page hands the link on to Parrot; the id and time stay after
/// the "#", which browsers never send to a server.
enum ParrotLink {

    static let scheme = "openparrot"
    static let handOff = "https://openparrot.app/open"

    /// The web link for AI apps (tool results).
    static func meeting(_ id: UUID, at time: TimeInterval? = nil) -> String {
        "\(handOff)#m=\(id.uuidString)" + (time.map { "&t=\(Int(max(0, $0)))" } ?? "")
    }

    /// Parrot's own link, which the hand-off page opens.
    static func app(_ id: UUID, at time: TimeInterval? = nil) -> String {
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
