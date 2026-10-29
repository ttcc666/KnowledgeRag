using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Documents;
using Microsoft.Extensions.Options;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class DocumentParserSecurityTests
{
    [Fact]
    public async Task TextParser_RejectsTextBeyondConfiguredCharacterLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            await File.WriteAllTextAsync(path, new string('x', 101));
            var parser = CreateParser(maxCharacters: 100);
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => parser.ExtractTextAsync(path));
            Assert.Contains("字符限制", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task TextParser_AcceptsContentAtConfiguredCharacterLimit()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.txt");
        try
        {
            const string expected = "0123456789";
            await File.WriteAllTextAsync(path, expected);
            Assert.Equal(expected, await CreateParser(expected.Length).ExtractTextAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task HtmlParser_RejectsOversizedInputBeforeRunningRegexes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        try
        {
            await File.WriteAllTextAsync(path, $"<html>{new string('x', 101)}</html>");
            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateParser(100).ExtractTextAsync(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Parser_RejectsInvalidCharacterAndPageLimits()
    {
        Assert.Throws<InvalidOperationException>(() => new DocumentParser(
            Options.Create(new RagOptions { MaxExtractedCharacters = 0, MaxPdfPages = 1 })));
        Assert.Throws<InvalidOperationException>(() => new DocumentParser(
            Options.Create(new RagOptions { MaxExtractedCharacters = 100, MaxPdfPages = 0 })));
    }

    private static DocumentParser CreateParser(int maxCharacters)
        => new(Options.Create(new RagOptions { MaxExtractedCharacters = maxCharacters, MaxPdfPages = 10 }));
}
