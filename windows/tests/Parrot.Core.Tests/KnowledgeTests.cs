// Parrot for Windows tests. Derived from Parrot (GPL-3.0), ProfileTest.swift (KB checks).
using Parrot.Core.Knowledge;

namespace Parrot.Core.Tests;

public class KnowledgeTests
{
    [Fact]
    public void ChunkText_GluesHeadingsAndDropsSeparators()
    {
        var text = "# Pricing\n\nThe Pro plan costs 49 dollars per seat per month, billed annually.\n\n---\n\n# Security\n\nAll data is encrypted at rest with AES-256 and in transit with TLS 1.3.";
        var chunks = TextTools.ChunkText(text, cap: 90);
        Assert.Equal(2, chunks.Count);
        Assert.StartsWith("# Pricing\nThe Pro plan", chunks[0]);
        Assert.StartsWith("# Security\nAll data", chunks[1]);
        Assert.DoesNotContain(chunks, c => c.Contains("---"));
    }

    [Fact]
    public void ChunkText_SplitsLongParagraphCarryingSection()
    {
        var lines = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"Line {i} of the refund policy explains a detail."));
        var chunks = TextTools.ChunkText("# Refunds\n" + lines, cap: 300);
        Assert.True(chunks.Count > 3);
        Assert.All(chunks, c => Assert.StartsWith("# Refunds\n", c));
        Assert.All(chunks, c => Assert.True(c.Length <= 300 + 60));
    }

    [Fact]
    public void ChunkText_DropsTinyChunks()
    {
        Assert.Empty(TextTools.ChunkText("hi\n\nok"));
    }

    [Fact]
    public void LexicalTokens_StemsAndDropsStopWords()
    {
        var t = TextTools.LexicalTokens("What is the pricing for 2025 integrations?");
        Assert.Equal(new[] { "prici", "2025", "integ" }, t);
    }

    [Fact]
    public void Bm25_RanksRelevantDocumentFirstAndSkipsNonMatching()
    {
        var docs = new List<IReadOnlyList<string>>
        {
            TextTools.LexicalTokens("Our office dog is called Biscuit."),
            TextTools.LexicalTokens("Pricing: the pro plan is 49 per seat. Enterprise pricing on request."),
            TextTools.LexicalTokens("Pricing changes are announced yearly."),
        };
        var order = TextTools.Bm25Order(TextTools.LexicalTokens("enterprise pricing"), docs);
        Assert.Equal(new[] { 1, 2 }, order);
    }

    [Fact]
    public void KnowledgeBase_SearchRespectsProfilesAndPersists()
    {
        using var paths = new TempPaths();
        var kb = new KnowledgeBase(paths);
        Assert.Null(kb.AddText("pricing.md", "# Pricing\n\nThe Pro plan costs 49 dollars per seat per month. Discounts start at 50 seats."));
        Assert.Null(kb.AddText("security.md", "# Security\n\nWe are SOC 2 Type II certified and encrypt all customer data at rest."));

        var hits = kb.Search("how much does the pro plan cost per seat");
        Assert.Equal("pricing.md", hits[0].DocumentName);

        // Scope the security doc to one profile: other profiles no longer see it.
        var profile = Guid.NewGuid();
        var sec = kb.Documents.Single(d => d.Name == "security.md");
        sec.ProfileIds = new List<Guid> { profile };
        sec.Note = "Use for security reviews";
        kb.Update(sec);
        Assert.Empty(kb.Search("SOC 2 certified", Guid.NewGuid()));
        var scoped = kb.Search("SOC 2 certified", profile);
        Assert.Equal("security.md", scoped[0].DocumentName);
        Assert.Equal("Use for security reviews", scoped[0].Note);

        // Reload from disk.
        var again = new KnowledgeBase(paths);
        Assert.Equal(2, again.Documents.Count);
        Assert.Equal("pricing.md", again.Search("pro plan seat price")[0].DocumentName);

        again.Remove(again.Documents.Single(d => d.Name == "pricing.md").Id);
        Assert.Empty(again.Search("pro plan seat price"));
    }

    [Fact]
    public void KnowledgeBase_ReAddKeepsNoteAndScope()
    {
        var kb = new KnowledgeBase(null);
        kb.AddText("faq.txt", "Shipping takes three to five business days within the country.");
        var doc = kb.Documents[0];
        doc.Note = "Customer FAQ";
        kb.Update(doc);
        kb.AddText("faq.txt", "Shipping takes two business days within the country, express is next day.");
        Assert.Single(kb.Documents);
        Assert.Equal("Customer FAQ", kb.Documents[0].Note);
        Assert.Equal(doc.Id, kb.Documents[0].Id);
    }

    [Fact]
    public void TextExtractor_ReadsTextFiles()
    {
        using var paths = new TempPaths();
        var file = Path.Combine(paths.Root, "notes.md");
        File.WriteAllText(file, "Hello knowledge base — café");
        Assert.Equal("Hello knowledge base — café", TextExtractor.Extract(file));
        Assert.False(TextExtractor.IsSupported("x.docx"));
    }
}
