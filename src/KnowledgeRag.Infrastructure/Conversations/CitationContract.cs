using System.Text.RegularExpressions;
using KnowledgeRag.Application.Contracts;

namespace KnowledgeRag.Infrastructure.Conversations;

public sealed record CitationNormalizationResult(
    string Answer,
    IReadOnlyList<SearchHit> Sources,
    int CitationCount,
    int InvalidCitationCount,
    bool HadCitationMarkers);

/// <summary>
/// Converts model-facing evidence references such as [E3] into stable user-facing
/// citations such as [1], while only exposing evidence that was actually cited.
/// Invalid evidence references are removed rather than being presented as trusted sources.
/// </summary>
public static partial class CitationContract
{
    [GeneratedRegex(@"\[(?:E|e)?(?<index>\d{1,3})\]", RegexOptions.CultureInvariant)]
    private static partial Regex CitationRegex();

    public static CitationNormalizationResult Normalize(string? answer, IReadOnlyList<SearchHit> evidence)
    {
        var text = answer ?? string.Empty;
        var matches = CitationRegex().Matches(text);
        if (matches.Count == 0)
            return new CitationNormalizationResult(text, [], 0, 0, false);

        var evidenceToCitation = new Dictionary<int, int>();
        var cited = new List<SearchHit>();
        var invalid = 0;

        var normalized = CitationRegex().Replace(text, match =>
        {
            if (!int.TryParse(match.Groups["index"].Value, out var evidenceNumber) ||
                evidenceNumber < 1 || evidenceNumber > evidence.Count)
            {
                invalid++;
                return string.Empty;
            }

            if (!evidenceToCitation.TryGetValue(evidenceNumber, out var citationNumber))
            {
                citationNumber = cited.Count + 1;
                evidenceToCitation[evidenceNumber] = citationNumber;
                cited.Add(evidence[evidenceNumber - 1]);
            }

            return $"[{citationNumber}]";
        });

        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ").Trim();
        return new CitationNormalizationResult(normalized, cited, evidenceToCitation.Count, invalid, true);
    }
}
