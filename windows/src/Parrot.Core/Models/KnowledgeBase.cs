// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Models/KnowledgeBase.swift.
namespace Parrot.Core.Models;

/// One chunk of a knowledge base document. The Mac embeds chunks on-device;
/// the Windows v1 matches on words only (BM25), so there is no vector here.
public sealed class KBChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DocumentName { get; set; } = "";
    public string Text { get; set; } = "";
}

/// A document the user added to the knowledge base.
public sealed class KBDocument
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    /// The "About" line, sent to the assistant as the source's user note.
    public string Note { get; set; } = "";
    public int ChunkCount { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    /// Call types (profile ids) this document is used on; empty = all.
    public List<Guid> ProfileIds { get; set; } = new();
    public bool Disabled { get; set; }

    public bool IsInPlay(Guid? profileId) =>
        !Disabled && (ProfileIds.Count == 0 || profileId is null || ProfileIds.Contains(profileId.Value));
}

/// A retrieved chunk handed to the analysis provider, joined with its document's note.
public sealed record KBReference(string DocumentName, string? Note, string Text);
