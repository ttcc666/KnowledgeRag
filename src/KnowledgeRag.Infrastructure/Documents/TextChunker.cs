using System.Text;
using System.Text.RegularExpressions;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Documents;

public sealed class TextChunker(IOptions<RagOptions> options)
{
    private readonly RagOptions _options = options.Value;
    private static readonly Regex PageMarker = new(@"\[\[PAGE:(\d+)\]\]", RegexOptions.Compiled);

    public IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (!PageMarker.IsMatch(text)) return SplitPlain(text);

        var matches = PageMarker.Matches(text);
        var result = new List<string>();
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var start = match.Index + match.Length;
            var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var pageText = text[start..end].Trim();
            if (string.IsNullOrWhiteSpace(pageText)) continue;
            var pageNumber = match.Groups[1].Value;
            foreach (var chunk in SplitPlain(pageText)) result.Add($"[[PAGE:{pageNumber}]]\n{chunk}");
        }
        return result;
    }

    private IReadOnlyList<string> SplitPlain(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var paragraphs = normalized.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        var chunks = new List<string>();
        var current = new StringBuilder();
        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length > _options.ChunkSize)
            {
                FlushCurrent();
                SplitLargeParagraph(paragraph, chunks);
                continue;
            }
            var additionalLength = current.Length == 0 ? paragraph.Length : paragraph.Length + 2;
            if (current.Length > 0 && current.Length + additionalLength > _options.ChunkSize)
            {
                var previous = current.ToString().Trim();
                chunks.Add(previous);
                current.Clear();
                var overlap = Tail(previous, _options.ChunkOverlap);
                if (!string.IsNullOrWhiteSpace(overlap)) current.Append(overlap).Append("\n\n");
            }
            current.Append(paragraph).Append("\n\n");
        }
        FlushCurrent();
        return chunks;
        void FlushCurrent()
        {
            var value = current.ToString().Trim();
            if (!string.IsNullOrWhiteSpace(value)) chunks.Add(value);
            current.Clear();
        }
    }

    private void SplitLargeParagraph(string paragraph, List<string> chunks)
    {
        var step = Math.Max(1, _options.ChunkSize - _options.ChunkOverlap);
        for (var start = 0; start < paragraph.Length; start += step)
        {
            var len = Math.Min(_options.ChunkSize, paragraph.Length - start);
            var value = paragraph.Substring(start, len).Trim();
            if (!string.IsNullOrWhiteSpace(value)) chunks.Add(value);
            if (start + len >= paragraph.Length) break;
        }
    }

    private static string Tail(string text, int count)
    {
        if (count <= 0 || string.IsNullOrEmpty(text)) return string.Empty;
        if (text.Length <= count) return text;
        return text[^count..];
    }
}
