using System.Data;
using System.Text.Json;
using KnowledgeRag.Application.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Configuration;
using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed class RagRunAccountingService(
    AppDbContext db,
    RagRuntimeProfileService runtimeProfiles,
    IOptions<AiOptions> aiOptions,
    IOptions<RerankerOptions> rerankerOptions)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan ReservationTtl = TimeSpan.FromHours(1);
    private readonly AiOptions _ai = aiOptions.Value;
    private readonly RerankerOptions _reranker = rerankerOptions.Value;

    public async Task<RagRunEntity> ReserveAsync(
        ActorContext actor,
        Guid knowledgeBaseId,
        Guid? conversationId,
        string query,
        ResolvedRagProfile profile,
        CancellationToken ct = default)
    {
        var tenantId = await db.KnowledgeBases.AsNoTracking()
            .Where(x => x.Id == knowledgeBaseId)
            .Select(x => (Guid?)x.TenantId)
            .SingleOrDefaultAsync(ct)
            ?? throw new KeyNotFoundException("知识库不存在。");

        var estimates = AiUsageEstimator.BuildReservation(profile, query, _ai, _reranker);
        var reservationLines = await PriceAsync(tenantId, estimates, ct);
        var reservationCost = reservationLines.Sum(x => x.CostUsd);
        var now = DateTimeOffset.UtcNow;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
                var rows = await db.RagRuns
                    .Where(x => x.TenantId == tenantId && x.KnowledgeBaseId == knowledgeBaseId)
                    .ToListAsync(ct);

                foreach (var stale in rows.Where(x =>
                             string.Equals(x.Status, "reserved", StringComparison.OrdinalIgnoreCase) &&
                             x.CreatedAt < now - ReservationTtl))
                {
                    stale.Status = "reservation-expired";
                    stale.EstimatedCostUsd = 0;
                    stale.TraceJson = JsonSerializer.Serialize(new[]
                    {
                        new { type = "usage.reservation.expired", expiredAt = now }
                    }, WebJson);
                }

                var snapshots = rows
                    .Where(x => !string.Equals(x.Status, "reservation-expired", StringComparison.OrdinalIgnoreCase))
                    .Select(x => new RagBudgetSnapshot(x.CreatedAt, x.Status, x.EstimatedCostUsd))
                    .ToList();
                RagBudgetPolicy.EnsureAllowed(profile, snapshots, reservationCost, now);

                var reservation = new RagRunEntity
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    KnowledgeBaseId = knowledgeBaseId,
                    ConversationId = conversationId,
                    UserId = actor.UserId,
                    ApiKeyId = actor.ApiKeyId,
                    Query = query,
                    Answer = string.Empty,
                    Status = "reserved",
                    AnswerModel = profile.AnswerModel,
                    AgentMode = profile.AgentMode,
                    SourceCount = 0,
                    EstimatedInputTokens = reservationLines.Sum(x => x.InputTokens),
                    EstimatedOutputTokens = reservationLines.Sum(x => x.OutputTokens),
                    EstimatedCostUsd = reservationCost,
                    LatencyMs = 0,
                    TraceJson = JsonSerializer.Serialize(new object[]
                    {
                        new
                        {
                            type = "usage.reservation",
                            estimated = true,
                            reservedCostUsd = reservationCost,
                            lineItems = reservationLines
                        }
                    }, WebJson),
                    CreatedAt = now
                };
                db.RagRuns.Add(reservation);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                return reservation;
            }
            catch (Exception ex) when (attempt < 2 && IsSerializationConflict(ex))
            {
                // Only detach accounting rows. Clearing the whole request DbContext would also
                // detach the Conversation that must be updated after the Agent finishes.
                foreach (var entry in db.ChangeTracker.Entries<RagRunEntity>().ToList())
                    entry.State = EntityState.Detached;
                await Task.Delay(25 * (attempt + 1), ct);
            }
        }

        throw new InvalidOperationException("无法完成 AI 预算预留，请重试。");
    }

    public async Task<RagRunEntity> SettleAsync(
        Guid runId,
        ResolvedRagProfile profile,
        string query,
        string answer,
        int sourceCount,
        long latencyMs,
        IReadOnlyList<object> trace,
        string status,
        CancellationToken ct = default)
    {
        var run = await db.RagRuns.SingleOrDefaultAsync(x => x.Id == runId, ct)
                  ?? throw new KeyNotFoundException("RAG Run 预留不存在。");
        var estimates = AiUsageEstimator.BuildObserved(profile, query, answer, sourceCount, trace, _ai, _reranker);
        var lineItems = await PriceAsync(run.TenantId, estimates, ct);
        var cost = lineItems.Sum(x => x.CostUsd);
        var inputTokens = lineItems.Sum(x => x.InputTokens);
        var outputTokens = lineItems.Sum(x => x.OutputTokens);
        var persistedTrace = trace.Concat(new object[]
        {
            new
            {
                type = "usage.ledger",
                estimated = true,
                inputTokens,
                outputTokens,
                costUsd = cost,
                lineItems
            }
        }).ToArray();

        run.Query = query;
        run.Answer = answer;
        run.Status = status;
        run.AnswerModel = profile.AnswerModel;
        run.AgentMode = profile.AgentMode;
        run.SourceCount = sourceCount;
        run.EstimatedInputTokens = inputTokens;
        run.EstimatedOutputTokens = outputTokens;
        run.EstimatedCostUsd = cost;
        run.LatencyMs = latencyMs;
        run.TraceJson = JsonSerializer.Serialize(persistedTrace, WebJson);
        await db.SaveChangesAsync(ct);
        return run;
    }

    private async Task<IReadOnlyList<AiUsageLineItem>> PriceAsync(Guid tenantId, IReadOnlyList<AiUsageEstimate> estimates, CancellationToken ct)
    {
        var explicitPrices = await db.ModelPrices.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IsActive)
            .ToListAsync(ct);
        var result = new List<AiUsageLineItem>(estimates.Count);

        foreach (var estimate in estimates)
        {
            decimal cost;
            if (string.Equals(estimate.Operation, "embedding", StringComparison.OrdinalIgnoreCase))
            {
                // Chat pricing is not a safe fallback for embedding models. Embedding cost is
                // zero until the tenant explicitly configures that model in Model Prices.
                var price = explicitPrices
                    .Where(x => string.Equals(x.Model, estimate.Model, StringComparison.Ordinal))
                    .OrderByDescending(x => x.UpdatedAt)
                    .FirstOrDefault();
                cost = price is null
                    ? 0
                    : estimate.InputTokens / 1_000_000m * price.InputCostPerMillionTokens +
                      estimate.OutputTokens / 1_000_000m * price.OutputCostPerMillionTokens;
            }
            else
            {
                cost = await runtimeProfiles.EstimateCostAsync(
                    tenantId,
                    estimate.Model,
                    estimate.InputTokens,
                    estimate.OutputTokens,
                    ct);
            }

            result.Add(new AiUsageLineItem(
                estimate.Operation,
                estimate.Model,
                estimate.Calls,
                estimate.InputTokens,
                estimate.OutputTokens,
                decimal.Round(cost, 8),
                estimate.Estimated));
        }
        return result;
    }

    private static bool IsSerializationConflict(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is PostgresException pg && pg.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
                return true;
            if (current is SqliteException sqlite && sqlite.SqliteErrorCode is 5 or 6)
                return true;
        }
        return false;
    }
}
