using System.Text.Json;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed record EvaluationExpectedSource(string FileName, int? ChunkIndex = null);
public sealed record EvaluationExpectation(string? AnswerContains, IReadOnlyList<EvaluationExpectedSource> Sources);

/// <summary>
/// Backward-compatible v3 expectation envelope stored in the existing ExpectedAnswerContains
/// column. Legacy plain-text values remain valid and require no database migration.
/// </summary>
public static class EvaluationExpectationCodec
{
    private const string Prefix = "@v3:";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static string? Serialize(string? answerContains, IReadOnlyList<EvaluationExpectedSource>? sources)
    {
        var answer = string.IsNullOrWhiteSpace(answerContains) ? null : answerContains.Trim();
        var normalizedSources = NormalizeSources(sources);
        if (normalizedSources.Count == 0) return answer;
        return Prefix + JsonSerializer.Serialize(new EvaluationExpectation(answer, normalizedSources), WebJson);
    }

    public static EvaluationExpectation Parse(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored)) return new EvaluationExpectation(null, []);
        if (!stored.StartsWith(Prefix, StringComparison.Ordinal))
            return new EvaluationExpectation(stored, []);

        try
        {
            var decoded = JsonSerializer.Deserialize<EvaluationExpectation>(stored[Prefix.Length..], WebJson);
            if (decoded is null) return new EvaluationExpectation(null, []);
            return decoded with { Sources = NormalizeSources(decoded.Sources) };
        }
        catch (JsonException)
        {
            // Corrupt envelopes must not make an evaluation run crash. Treat them as a legacy
            // expected-answer string so the failure remains visible in deterministic scoring.
            return new EvaluationExpectation(stored, []);
        }
    }

    private static IReadOnlyList<EvaluationExpectedSource> NormalizeSources(IEnumerable<EvaluationExpectedSource>? sources)
        => (sources ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x.FileName))
            .Select(x => new EvaluationExpectedSource(x.FileName.Trim(), x.ChunkIndex))
            .DistinctBy(x => (x.FileName.ToLowerInvariant(), x.ChunkIndex))
            .Take(50)
            .ToList();
}
