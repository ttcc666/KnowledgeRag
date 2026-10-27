using System.Security.Claims;
using KnowledgeRag.Api.Security;

namespace KnowledgeRag.Api.Endpoints;

public static class DocumentEndpoints
{
    public static IEndpointRouteBuilder MapDocumentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/knowledge-bases/{knowledgeBaseId:guid}/documents")
            .RequireAuthorization()
            .WithTags("Documents");

        group.MapGet("", async (Guid knowledgeBaseId, ClaimsPrincipal principal, KnowledgeBaseAccessService access, KnowledgeIngestionService ingestion, CancellationToken ct) =>
        {
            var actor = HttpActorContext.From(principal);
            await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Viewer, ct);
            var documents = await ingestion.ListAsync(knowledgeBaseId, ct);
            return Results.Ok(documents.Select(d => new
            {
                d.Id, d.FileName, d.ContentType, d.SizeBytes, d.ChunkCount, d.Sha256,
                d.Status, d.LastError, d.CreatedAt, d.UpdatedAt
            }));
        });

        group.MapPost("", async (Guid knowledgeBaseId, IFormFile file, ClaimsPrincipal principal, KnowledgeBaseAccessService access, KnowledgeIngestionService ingestion, CancellationToken ct) =>
        {
            var actor = HttpActorContext.From(principal);
            await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Editor, ct);
            var document = await ingestion.IngestAsync(knowledgeBaseId, actor.UserId, file, ct);
            return Results.Ok(new UploadResponse(document.Id, document.FileName, document.ChunkCount, document.Sha256, document.Status));
        }).DisableAntiforgery();

        group.MapGet("/{documentId:guid}/file", async (Guid knowledgeBaseId, Guid documentId, ClaimsPrincipal principal, KnowledgeBaseAccessService access, KnowledgeIngestionService ingestion, CancellationToken ct) =>
        {
            var actor = HttpActorContext.From(principal);
            await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Viewer, ct);
            var download = await ingestion.OpenReadAsync(knowledgeBaseId, documentId, ct);
            return download is null
                ? Results.NotFound()
                : Results.Stream(download.Stream, download.ContentType, download.FileName, enableRangeProcessing: false);
        });

        group.MapDelete("/{documentId:guid}", async (Guid knowledgeBaseId, Guid documentId, ClaimsPrincipal principal, KnowledgeBaseAccessService access, KnowledgeIngestionService ingestion, CancellationToken ct) =>
        {
            var actor = HttpActorContext.From(principal);
            await access.RequireAsync(actor, knowledgeBaseId, KnowledgeBaseRole.Editor, ct);
            return await ingestion.DeleteAsync(knowledgeBaseId, documentId, ct) ? Results.NoContent() : Results.NotFound();
        });

        return app;
    }
}
