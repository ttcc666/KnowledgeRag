using System.Collections.Concurrent;
using KnowledgeRag.Application.Contracts;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Observability;

public sealed class ProductMetricsCollector(IOptions<AiOptions> aiOptions)
{
    private const int MaxSamples = 2000;
    private readonly ConcurrentQueue<SearchSample> _search = new();
    private readonly AiOptions _ai = aiOptions.Value;
    private long _chatTurns;
    private long _estimatedInputTokens;
    private long _estimatedOutputTokens;

    public void RecordSearch(double latencyMs, int resultCount, double topScore, double? averageRerankScore = null)
    {
        _search.Enqueue(new SearchSample(DateTimeOffset.UtcNow, latencyMs, resultCount, topScore, averageRerankScore));
        Trim(_search);
    }

    public void RecordChat(string input, string output)
    {
        Interlocked.Increment(ref _chatTurns);
        Interlocked.Add(ref _estimatedInputTokens, EstimateTokens(input));
        Interlocked.Add(ref _estimatedOutputTokens, EstimateTokens(output));
    }

    public RetrievalMetricsSummary Snapshot(TimeSpan? window = null)
    {
        var cutoff = DateTimeOffset.UtcNow - (window ?? TimeSpan.FromHours(24));
        var samples = _search.Where(x => x.At >= cutoff).ToArray();
        var latencies = samples.Select(x => x.LatencyMs).OrderBy(x => x).ToArray();
        var scores = samples.Where(x => x.ResultCount > 0).Select(x => x.TopScore).ToArray();
        var rerankScores = samples.Where(x => x.AverageRerankScore.HasValue).Select(x => x.AverageRerankScore!.Value).ToArray();
        var inputTokens = Interlocked.Read(ref _estimatedInputTokens);
        var outputTokens = Interlocked.Read(ref _estimatedOutputTokens);
        var cost = EstimateCost(inputTokens, outputTokens);
        return new RetrievalMetricsSummary(
            samples.LongLength,
            Percentile(latencies, .50),
            Percentile(latencies, .95),
            samples.Length == 0 ? 0 : samples.Count(x => x.ResultCount == 0) / (double)samples.Length,
            scores.Length == 0 ? 0 : scores.Average(),
            rerankScores.Length == 0 ? 0 : rerankScores.Average(),
            inputTokens,
            outputTokens,
            cost,
            Interlocked.Read(ref _chatTurns));
    }

    private decimal? EstimateCost(long inputTokens, long outputTokens)
    {
        if (_ai.ChatInputCostPerMillionTokens <= 0 && _ai.ChatOutputCostPerMillionTokens <= 0) return null;
        return decimal.Round(
            (inputTokens / 1_000_000m) * _ai.ChatInputCostPerMillionTokens +
            (outputTokens / 1_000_000m) * _ai.ChatOutputCostPerMillionTokens,
            6);
    }

    private static long EstimateTokens(string? text) => string.IsNullOrWhiteSpace(text) ? 0 : Math.Max(1, (long)Math.Ceiling(text.Length / 3.5));

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0) return 0;
        var index = (values.Length - 1) * percentile;
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        if (lower == upper) return values[lower];
        return values[lower] + (values[upper] - values[lower]) * (index - lower);
    }

    private static void Trim(ConcurrentQueue<SearchSample> queue)
    {
        while (queue.Count > MaxSamples) queue.TryDequeue(out _);
    }

    private sealed record SearchSample(DateTimeOffset At, double LatencyMs, int ResultCount, double TopScore, double? AverageRerankScore);
}
