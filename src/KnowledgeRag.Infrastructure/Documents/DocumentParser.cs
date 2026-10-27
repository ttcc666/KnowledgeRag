using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using UglyToad.PdfPig;

namespace KnowledgeRag.Infrastructure.Documents;

public sealed class DocumentParser
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".csv", ".json", ".xml", ".html", ".htm", ".pdf", ".docx"
    };

    public bool Supports(string fileName) => SupportedExtensions.Contains(Path.GetExtension(fileName));

    public async Task<string> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();

        return extension switch
        {
            ".pdf" => ExtractPdf(filePath),
            ".docx" => ExtractDocx(filePath),
            ".html" or ".htm" => StripHtml(await File.ReadAllTextAsync(filePath, cancellationToken)),
            _ => await File.ReadAllTextAsync(filePath, cancellationToken)
        };
    }

    private static string ExtractPdf(string filePath)
    {
        using var document = PdfDocument.Open(filePath);
        var builder = new StringBuilder();
        foreach (var page in document.GetPages())
        {
            builder.AppendLine(page.Text);
            builder.AppendLine();
        }
        return builder.ToString();
    }

    private static string ExtractDocx(string filePath)
    {
        using var document = WordprocessingDocument.Open(filePath, false);
        return document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;
    }

    private static string StripHtml(string html)
    {
        var withoutScripts = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var withoutTags = Regex.Replace(withoutScripts, "<[^>]+>", " ");
        return System.Net.WebUtility.HtmlDecode(Regex.Replace(withoutTags, "\\s+", " "));
    }
}
