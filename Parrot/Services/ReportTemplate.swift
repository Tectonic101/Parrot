import Foundation

/// What a profile's end-of-call report looks like: its sections and its
/// coaching lens. Same shape as the `report` block of a `.parrotprofile`
/// (ProfileFile carries it as is). `.standard` is the report every profile
/// had before Profiles 2.0; its prompts are that report word for word
/// (golden-tested), so nobody who never touches templates sees a change.
struct ReportTemplate: Codable, Equatable {
    var sections: [Section]
    /// nil = the standard coaching (on, "sales/meeting coach", no focus).
    var coaching: Coaching?

    struct Section: Codable, Equatable {
        var key, title: String
        /// "prose" (a short paragraph), "bullets" or "scorecard".
        var type: String
        /// "What goes here", in the user's words.
        var guide: String?
        /// Bullets must be things someone said; they feed list_commitments,
        /// open items, Reminders and the receipts check.
        var commitments: Bool?
        var criteria: [Criterion]?
    }

    struct Criterion: Codable, Equatable {
        var key, label: String
        var guide: String?
    }

    struct Coaching: Codable, Equatable {
        var enabled: Bool
        var role: String?
        var focus: String?
    }

    static let maxSections = 8

    static let standard = ReportTemplate(sections: [
        Section(key: "overview", title: "Overview", type: "prose",
                guide: "2-3 sentences: what the call was about and how it ended."),
        Section(key: "pain", title: "Pain points", type: "bullets",
                guide: "What the other side is struggling with, what they're trying to achieve, and why."),
        Section(key: "key", title: "Key points", type: "bullets", guide: "Short bullets on what mattered."),
        Section(key: "next", title: "Next steps", type: "bullets",
                guide: "What someone said they'd do, with any date.", commitments: true),
    ], coaching: nil)

    static let standardCoachRole = "sales/meeting coach"

    var isStandard: Bool { self == .standard }
    var coachingEnabled: Bool { coaching?.enabled ?? true }

    var coachRole: String {
        let role = coaching?.role?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
        return role.isEmpty ? Self.standardCoachRole : role
    }

    var coachFocus: String {
        coaching?.focus?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
    }

    /// Section titles, for the report parser to recognise as headings.
    var titles: [String] { sections.map(\.title).filter { !$0.isEmpty } }

    /// The template's own answer for a section title: its `commitments`
    /// flag, or nil when the title isn't one of its sections (coaching
    /// sections, reports written before the template).
    func commitmentFlag(forTitle title: String) -> Bool? {
        let t = Self.normalized(title)
        return sections.first { Self.normalized($0.title) == t }.map { $0.commitments == true }
    }

    private static func normalized(_ s: String) -> String {
        s.trimmingCharacters(in: .whitespacesAndNewlines.union(CharacterSet(charactersIn: ":*#"))).lowercased()
    }

    /// "a" or "an" before the coach role.
    // ponytail: first letter only, so "an user coach"; fine for roles people write.
    static func article(for word: String) -> String {
        guard let first = word.lowercased().first else { return "a" }
        return "aeiou".contains(first) ? "an" : "a"
    }

    /// The summary prompt's structure paragraph for a custom template: one
    /// line per section, title first, so a small local model can follow it.
    var summaryStructure: String {
        // A section still being typed (no title yet) isn't asked for.
        let named = sections.filter { !$0.title.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }
        let lines = named.map { s -> String in
            var line = "\(s.title): " + (s.type == "prose" ? "one short paragraph, no bullets." : "\"-\" bullets, each ending with its [mm:ss].")
            if let guide = s.guide?.trimmingCharacters(in: .whitespacesAndNewlines), !guide.isEmpty {
                line += " " + guide
            }
            if s.commitments == true {
                line += " Only what a person actually said they will do; if unsure, leave it out."
            }
            return line
        }
        return "Output exactly these sections, in this order, each title on its own line followed by a colon:\n"
            + lines.joined(separator: "\n")
            + "\nWrite only these sections, no others. If a section has nothing, write \"- None surfaced\" under its title. Use plain text with "
            + "simple \"-\" bullets, no markdown headers. Write in the same language as the conversation."
    }
}
