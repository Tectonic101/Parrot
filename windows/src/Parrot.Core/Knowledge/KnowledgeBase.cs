// Parrot for Windows. Derived from Parrot (GPL-3.0), Parrot/Services/KnowledgeBaseService.swift.
using Parrot.Core.Models;
using Parrot.Core.Storage;

namespace Parrot.Core.Knowledge;

/// Local knowledge base: documents are chunked and matched against the live
/// conversation with BM25. Documents never leave the PC; only the few best-matching
/// chunks are later included in assistant requests.
/// (The Mac adds on-device sentence embeddings; Windows v1 is lexical only.)
public sealed class KnowledgeBase
{
    private sealed class Store
    {
        public int Version { get; set; } = 1;
        public List<KBDocument> Documents { get; set; } = new();
        public List<KBChunk> Chunks { get; set; } = new();
    }

    private readonly string? _file;
    private readonly object _lock = new();
    private List<KBDocument> _documents = new();
    private List<KBChunk> _chunks = new();
    private List<List<string>>? _tokenCache;

    public event Action? Changed;

    public KnowledgeBase(IAppPaths? paths)
    {
        if (paths == null) return;
        _file = paths.KnowledgeFile();
        try
        {
            var store = JsonFile.Read<Store>(_file);
            if (store != null) { _documents = store.Documents; _chunks = store.Chunks; }
        }
        catch (Exception)
        {
            // Unreadable store: keep it on disk, start empty but never save over it.
            _file = null;
        }
    }

    public IReadOnlyList<KBDocument> Documents { get { lock (_lock) return _documents.ToList(); } }
    public bool IsEmpty { get { lock (_lock) return _documents.Count == 0; } }

    /// Adds (or replaces, by file name) a document. Returns an error message or null.
    public string? AddDocument(string path)
    {
        var name = Path.GetFileName(path);
        if (!TextExtractor.IsSupported(path)) return $"{name}: only .txt, .md and .pdf are supported";
        var text = TextExtractor.Extract(path);
        if (string.IsNullOrWhiteSpace(text)) return $"Couldn't read {name}";
        return AddText(name, text);
    }

    public string? AddText(string name, string text)
    {
        var pieces = TextTools.ChunkText(text);
        if (pieces.Count == 0) return $"No text to index in {name}";
        lock (_lock)
        {
            var old = _documents.FirstOrDefault(d => d.Name == name);
            _chunks.RemoveAll(c => c.DocumentName == name);
            _chunks.AddRange(pieces.Select(p => new KBChunk { DocumentName = name, Text = p }));
            var doc = new KBDocument { Name = name, ChunkCount = pieces.Count, AddedAt = DateTime.Now };
            if (old != null)
            {
                // Re-adding a file is an update: keep its About line and Use for.
                doc.Id = old.Id; doc.Note = old.Note; doc.ProfileIds = old.ProfileIds; doc.Disabled = old.Disabled;
                _documents.Remove(old);
            }
            _documents.Add(doc);
            _tokenCache = null;
            Save();
        }
        Changed?.Invoke();
        return null;
    }

    public void Remove(Guid documentId)
    {
        lock (_lock)
        {
            var doc = _documents.FirstOrDefault(d => d.Id == documentId);
            if (doc == null) return;
            _documents.Remove(doc);
            _chunks.RemoveAll(c => c.DocumentName == doc.Name);
            _tokenCache = null;
            Save();
        }
        Changed?.Invoke();
    }

    public void Update(KBDocument document)
    {
        lock (_lock)
        {
            var i = _documents.FindIndex(d => d.Id == document.Id);
            if (i < 0) return;
            _documents[i] = document;
            Save();
        }
        Changed?.Invoke();
    }

    /// Document names the assistant may quote on a call of this type.
    public List<string> DocumentsInPlay(Guid? profileId)
    {
        lock (_lock) return _documents.Where(d => d.IsInPlay(profileId)).Select(d => d.Name).ToList();
    }

    /// Best-matching chunks for the query, joined with each document's note.
    public List<KBReference> Search(string query, Guid? profileId = null, int topK = 4)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<KBReference>();
        lock (_lock)
        {
            if (_chunks.Count == 0) return new List<KBReference>();
            _tokenCache ??= _chunks.Select(c => TextTools.LexicalTokens(c.Text)).ToList();
            var allowed = new HashSet<string>(_documents.Where(d => d.IsInPlay(profileId)).Select(d => d.Name));
            var indices = Enumerable.Range(0, _chunks.Count).Where(i => allowed.Contains(_chunks[i].DocumentName)).ToList();
            if (indices.Count == 0) return new List<KBReference>();
            var docs = indices.Select(i => (IReadOnlyList<string>)_tokenCache[i]).ToList();
            var order = TextTools.Bm25Order(TextTools.LexicalTokens(query), docs);
            var notes = _documents.GroupBy(d => d.Name).ToDictionary(g => g.Key, g => g.First().Note);
            return order.Take(topK).Select(o =>
            {
                var chunk = _chunks[indices[o]];
                var note = notes.GetValueOrDefault(chunk.DocumentName);
                return new KBReference(chunk.DocumentName, string.IsNullOrEmpty(note) ? null : note, chunk.Text);
            }).ToList();
        }
    }

    private void Save()
    {
        if (_file == null) return;
        JsonFile.Write(_file, new Store { Documents = _documents, Chunks = _chunks });
    }
}
