using System.Text;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Documents;

public sealed class TextChunker(IOptions<RagOptions> options)
{
    private readonly RagOptions _options = options.Value;

    public IReadOnlyList<string> Split(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var paragraphs = normalized
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();

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
