// Parrot for Windows. Derived from Parrot (GPL-3.0), KnowledgeBaseService.extractText.
using System.Text;
using UglyToad.PdfPig;

namespace Parrot.Core.Knowledge;

public static class TextExtractor
{
    public static readonly string[] SupportedExtensions = { ".txt", ".md", ".markdown", ".text", ".pdf" };

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// Plain text of a .txt/.md/.pdf file, or null when unreadable.
    public static string? Extract(string path)
    {
        try
        {
            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
                return ExtractPdf(path);
            var bytes = File.ReadAllBytes(path);
            try
            {
                return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('﻿');
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Latin1.GetString(bytes);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// One paragraph per page (pages separated by blank lines so the chunker splits there).
    private static string ExtractPdf(string path)
    {
        using var doc = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            var text = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
            if (string.IsNullOrWhiteSpace(text)) continue;
            sb.Append(text.Trim()).Append("\n\n");
        }
        return sb.ToString();
    }
}
