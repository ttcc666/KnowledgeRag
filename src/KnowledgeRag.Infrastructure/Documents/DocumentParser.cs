using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig;

namespace KnowledgeRag.Infrastructure.Documents;

public sealed class DocumentParser
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".json", ".xml", ".html", ".htm", ".pdf", ".docx"
    };
    private static readonly TimeSpan HtmlRegexTimeout = TimeSpan.FromSeconds(2);
    private static readonly Regex ScriptStyleRegex = new(
        "<(script|style)\\b[^>]*>.*?</\\1\\s*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline,
        HtmlRegexTimeout);
    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.None, HtmlRegexTimeout);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.None, HtmlRegexTimeout);

    private readonly int _maxExtractedCharacters;
    private readonly int _maxPdfPages;

    public DocumentParser(IOptions<RagOptions> ragOptions)
    {
        var options = ragOptions.Value;
        if (options.MaxExtractedCharacters is < 1 or > 10_000_000)
            throw new InvalidOperationException("Rag:MaxExtractedCharacters 必须在 1 到 10000000 之间。");
        if (options.MaxPdfPages is < 1 or > 10_000)
            throw new InvalidOperationException("Rag:MaxPdfPages 必须在 1 到 10000 之间。");
        _maxExtractedCharacters = options.MaxExtractedCharacters;
        _maxPdfPages = options.MaxPdfPages;
    }

    public bool Supports(string fileName) => SupportedExtensions.Contains(Path.GetExtension(fileName));

    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var text = extension switch
        {
            ".pdf" => ExtractPdf(filePath, cancellationToken),
            ".docx" => ExtractDocx(filePath),
            ".html" or ".htm" => StripHtml(await ReadTextBoundedAsync(filePath, cancellationToken)),
            _ => await ReadTextBoundedAsync(filePath, cancellationToken)
        };

        EnsureWithinLimit(text.Length);
        return text;
    }

    private string ExtractPdf(string filePath, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Open(filePath);
        if (document.NumberOfPages > _maxPdfPages)
            throw new InvalidOperationException($"PDF 页数超过 {_maxPdfPages} 页限制。");

        var builder = new StringBuilder(Math.Min(_maxExtractedCharacters, 8192));
        var pageNumber = 0;
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            pageNumber++;
            var pageText = page.Text ?? string.Empty;
            var prefix = $"[[PAGE:{pageNumber}]] {Environment.NewLine}";
            var suffix = Environment.NewLine + Environment.NewLine;
            if (builder.Length + prefix.Length + pageText.Length + suffix.Length > _maxExtractedCharacters)
                throw new InvalidOperationException($"解析后文本超过 {_maxExtractedCharacters:N0} 字符限制。");
            builder.Append(prefix).Append(pageText).Append(suffix);
        }

        return builder.ToString();
    }

    private string ExtractDocx(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false, new OpenSettings
        {
            MaxCharactersInPart = _maxExtractedCharacters
        });
        return document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    }

    private async Task<string> ReadTextBoundedAsync(string filePath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 8192,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var builder = new StringBuilder(Math.Min(_maxExtractedCharacters, 8192));
        var buffer = new char[8192];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            if (builder.Length + read > _maxExtractedCharacters)
                throw new InvalidOperationException($"解析后文本超过 {_maxExtractedCharacters:N0} 字符限制。");
            builder.Append(buffer, 0, read);
        }

        return builder.ToString();
    }

    private string StripHtml(string html)
    {
        var withoutScripts = ScriptStyleRegex.Replace(html, " ");
        var withoutTags = HtmlTagRegex.Replace(withoutScripts, " ");
        return System.Net.WebUtility.HtmlDecode(WhitespaceRegex.Replace(withoutTags, " "));
    }

    private void EnsureWithinLimit(int characterCount)
    {
        if (characterCount > _maxExtractedCharacters)
            throw new InvalidOperationException($"解析后文本超过 {_maxExtractedCharacters:N0} 字符限制。");
    }
}
