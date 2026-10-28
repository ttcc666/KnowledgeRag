using System.Security.Claims;
using KnowledgeRag.Api.Security;
using KnowledgeRag.Domain.Entities;
using KnowledgeRag.Infrastructure.Governance;
using KnowledgeRag.Infrastructure.Tenancy;

namespace KnowledgeRag.Api.Endpoints;

public sealed record EvaluationCaseCreateV3(
    string Category,
    string Query,
    string? ExpectedAnswerContains,
    bool RequiresKnowledge = true,
    IReadOnlyList<EvaluationExpectedSource>? ExpectedSources = null);

public static class GovernanceEndpoints
{
    public static IEndpointRouteBuilder MapGovernanceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/governance").RequireAuthorization().WithTags("Governance");

        group.MapGet("/overview", async (Guid tenantId, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.GetOverviewAsync(HttpActorContext.From(principal), tenantId, ct)));

        group.MapGet("/knowledge-bases/{knowledgeBaseId:guid}/rag-profile", async (Guid knowledgeBaseId, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.GetRagProfileAsync(HttpActorContext.From(principal), knowledgeBaseId, ct)));

        group.MapPut("/knowledge-bases/{knowledgeBaseId:guid}/rag-profile", async (Guid knowledgeBaseId, ClaimsPrincipal principal, RagProfileUpdate request, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.SaveRagProfileAsync(HttpActorContext.From(principal), knowledgeBaseId, request, ct)));

        group.MapGet("/runs", async (Guid knowledgeBaseId, int? limit, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.ListRunsAsync(HttpActorContext.From(principal), knowledgeBaseId, limit ?? 100, ct)));

        group.MapGet("/audit", async (Guid tenantId, int? limit, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.ListAuditAsync(HttpActorContext.From(principal), tenantId, limit ?? 200, ct)));

        group.MapGet("/model-prices", async (Guid tenantId, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.ListModelPricesAsync(HttpActorContext.From(principal), tenantId, ct)));

        group.MapPut("/model-prices", async (Guid tenantId, ClaimsPrincipal principal, ModelPriceUpdate request, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.SaveModelPriceAsync(HttpActorContext.From(principal), tenantId, request, ct)));

        group.MapGet("/prompts", async (Guid tenantId, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.ListPromptProfilesAsync(HttpActorContext.From(principal), tenantId, ct)));

        group.MapPost("/prompts", async (Guid tenantId, ClaimsPrincipal principal, PromptProfileUpsert request, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.SavePromptProfileAsync(HttpActorContext.From(principal), tenantId, request, ct)));

        group.MapPut("/prompts/{promptId:guid}", async (Guid tenantId, Guid promptId, ClaimsPrincipal principal, PromptProfileUpsert request, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.SavePromptProfileAsync(HttpActorContext.From(principal), tenantId, request with { Id = promptId }, ct)));

        group.MapGet("/webhooks/providers", () => Results.Ok(WebhookChannelAdapterRegistry.Providers));

        group.MapGet("/webhooks", async (Guid tenantId, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
        {
            var hooks = await service.ListWebhooksAsync(HttpActorContext.From(principal), tenantId, ct);
            return Results.Ok(hooks.Select(ToWebhookResponse));
        });

        group.MapPost("/webhooks", async (Guid tenantId, ClaimsPrincipal principal, WebhookUpsert request, GovernanceService service, WebhookSecretProtector protector, CancellationToken ct) =>
        {
            var protectedRequest = request with { Secret = string.IsNullOrWhiteSpace(request.Secret) ? request.Secret : protector.Protect(request.Secret) };
            return Results.Ok(ToWebhookResponse(await service.SaveWebhookAsync(HttpActorContext.From(principal), tenantId, protectedRequest, ct)));
        });

        group.MapPut("/webhooks/{webhookId:guid}", async (Guid tenantId, Guid webhookId, ClaimsPrincipal principal, WebhookUpsert request, GovernanceService service, WebhookSecretProtector protector, CancellationToken ct) =>
        {
            var protectedRequest = request with { Id = webhookId, Secret = string.IsNullOrWhiteSpace(request.Secret) ? request.Secret : protector.Protect(request.Secret) };
            return Results.Ok(ToWebhookResponse(await service.SaveWebhookAsync(HttpActorContext.From(principal), tenantId, protectedRequest, ct)));
        });

        group.MapDelete("/webhooks/{webhookId:guid}", async (Guid tenantId, Guid webhookId, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
        {
            await service.DeleteWebhookAsync(HttpActorContext.From(principal), tenantId, webhookId, ct);
            return Results.NoContent();
        });

        group.MapPost("/webhooks/{webhookId:guid}/test", async (Guid tenantId, Guid webhookId, ClaimsPrincipal principal, TenantAccessService access, WebhookAutomationService webhooks, CancellationToken ct) =>
        {
            await access.RequireAsync(HttpActorContext.From(principal), tenantId, TenantRole.Admin, ct);
            try
            {
                await webhooks.TestAsync(tenantId, webhookId, ct);
                return Results.Accepted();
            }
            catch (WebhookDeliveryTimeoutException ex)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status504GatewayTimeout,
                    title: "Webhook 请求超时",
                    detail: ex.Message);
            }
        });

        group.MapGet("/evaluation/datasets", async (Guid tenantId, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.ListEvaluationDatasetsAsync(HttpActorContext.From(principal), tenantId, ct)));

        group.MapPost("/evaluation/datasets", async (Guid tenantId, ClaimsPrincipal principal, EvaluationDatasetCreate request, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.CreateEvaluationDatasetAsync(HttpActorContext.From(principal), tenantId, request, ct)));

        group.MapGet("/evaluation/datasets/{datasetId:guid}/cases", async (Guid tenantId, Guid datasetId, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
        {
            var rows = await service.ListEvaluationCasesAsync(HttpActorContext.From(principal), tenantId, datasetId, ct);
            return Results.Ok(rows.Select(ToEvaluationCaseResponse));
        });

        group.MapPost("/evaluation/datasets/{datasetId:guid}/cases", async (Guid tenantId, Guid datasetId, ClaimsPrincipal principal, EvaluationCaseCreateV3 request, GovernanceService service, CancellationToken ct) =>
        {
            var storedExpectation = EvaluationExpectationCodec.Serialize(request.ExpectedAnswerContains, request.ExpectedSources);
            var row = await service.AddEvaluationCaseAsync(
                HttpActorContext.From(principal),
                tenantId,
                datasetId,
                new EvaluationCaseCreate(request.Category, request.Query, storedExpectation, request.RequiresKnowledge),
                ct);
            return Results.Ok(ToEvaluationCaseResponse(row));
        });

        group.MapGet("/evaluation/runs", async (Guid tenantId, Guid? datasetId, ClaimsPrincipal principal, GovernanceService service, CancellationToken ct) =>
            Results.Ok(await service.ListEvaluationRunsAsync(HttpActorContext.From(principal), tenantId, datasetId, ct)));

        group.MapPost("/evaluation/datasets/{datasetId:guid}/run", async (Guid tenantId, Guid datasetId, ClaimsPrincipal principal, EvaluationService service, CancellationToken ct) =>
            Results.Ok(await service.RunAsync(HttpActorContext.From(principal), tenantId, datasetId, ct)));

        return app;
    }

    private static object ToEvaluationCaseResponse(EvaluationCaseEntity item)
    {
        var expectation = EvaluationExpectationCodec.Parse(item.ExpectedAnswerContains);
        return new
        {
            item.Id,
            item.DatasetId,
            item.Category,
            item.Query,
            ExpectedAnswerContains = expectation.AnswerContains,
            ExpectedSources = expectation.Sources,
            item.RequiresKnowledge,
            item.CreatedAt
        };
    }

    private static object ToWebhookResponse(WebhookSubscriptionEntity hook) => new
    {
        hook.Id,
        hook.TenantId,
        hook.Name,
        hook.Provider,
        hook.Url,
        hook.EventTypes,
        hook.IsActive,
        hook.LastStatusCode,
        hook.LastError,
        hook.LastDeliveredAt,
        hook.CreatedByUserId,
        hook.CreatedAt,
        hook.UpdatedAt,
        hasSecret = !string.IsNullOrWhiteSpace(hook.Secret)
    };
}
