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
/// Converts model-facing evidence references such as [E3] or [E1, E2] into stable
/// user-facing citations such as [1] [2], while only exposing evidence that was actually cited.
/// Invalid evidence references are removed rather than being presented as trusted sources.
/// </summary>
public static partial class CitationContract
{
    [GeneratedRegex(@"\[(?<refs>(?:[Ee]?\d{1,3})(?:\s*,\s*(?:[Ee]?\d{1,3}))*)\]", RegexOptions.CultureInvariant)]
    private static partial Regex CitationGroupRegex();

    [GeneratedRegex(@"\[(?:E|e)\d{1,3}[^\]\r\n]{0,64}\]", RegexOptions.CultureInvariant)]
    private static partial Regex MalformedEvidenceRegex();

    public static CitationNormalizationResult Normalize(string? answer, IReadOnlyList<SearchHit> evidence)
    {
        var text = answer ?? string.Empty;
        var matches = CitationGroupRegex().Matches(text);
        var malformedMatches = MalformedEvidenceRegex().Matches(text);
        if (matches.Count == 0 && malformedMatches.Count == 0)
            return new CitationNormalizationResult(text, [], 0, 0, false);

        var evidenceToCitation = new Dictionary<int, int>();
        var cited = new List<SearchHit>();
        var invalid = 0;

        var normalized = CitationGroupRegex().Replace(text, match =>
        {
            var emitted = new HashSet<int>();
            var output = new List<string>();
            foreach (var token in match.Groups["refs"].Value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var raw = token;
                if (raw.Length > 0 && (raw[0] == 'E' || raw[0] == 'e')) raw = raw[1..];

                if (!int.TryParse(raw, out var evidenceNumber) || evidenceNumber < 1 || evidenceNumber > evidence.Count)
                {
                    invalid++;
                    continue;
                }

                if (!evidenceToCitation.TryGetValue(evidenceNumber, out var citationNumber))
                {
                    citationNumber = cited.Count + 1;
                    evidenceToCitation[evidenceNumber] = citationNumber;
                    cited.Add(evidence[evidenceNumber - 1]);
                }

                if (emitted.Add(citationNumber)) output.Add($"[{citationNumber}]");
            }

            return string.Join(" ", output);
        });

        normalized = MalformedEvidenceRegex().Replace(normalized, _ =>
        {
            invalid++;
            return string.Empty;
        });
        normalized = Regex.Replace(normalized, @"[ \t]{2,}", " ").Trim();
        return new CitationNormalizationResult(normalized, cited, evidenceToCitation.Count, invalid, true);
    }
}
