using System.Security.Claims;
using System.Text.Json;
using KnowledgeRag.Api.Security;
using KnowledgeRag.Application.Contracts;

namespace KnowledgeRag.Api.Endpoints;

public static class ConversationEndpoints
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapConversationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/conversations").RequireAuthorization().WithTags("Conversations");

        group.MapPost("", async (CreateConversationRequest request, ClaimsPrincipal principal, ConversationService service, CancellationToken ct) =>
        {
            var conversation = await service.CreateAsync(HttpActorContext.From(principal), request, ct);
            return Results.Ok(new { conversation.Id, conversation.KnowledgeBaseId, conversation.Title, conversation.CreatedAt, conversation.UpdatedAt });
        });

        group.MapGet("", async (Guid? knowledgeBaseId, ClaimsPrincipal principal, ConversationService service, CancellationToken ct) =>
        {
            var rows = await service.ListAsync(HttpActorContext.From(principal), knowledgeBaseId, ct);
            return Results.Ok(rows.Select(x => new { x.Id, x.KnowledgeBaseId, x.Title, x.CreatedAt, x.UpdatedAt }));
        });

        group.MapGet("/{conversationId:guid}/messages", async (Guid conversationId, ClaimsPrincipal principal, ConversationService service, CancellationToken ct) =>
        {
            var messages = await service.GetMessagesAsync(HttpActorContext.From(principal), conversationId, ct);
            return Results.Ok(messages.Select(x => new { x.Id, x.Role, x.Content, SourcesJson = NormalizeSourcesJson(x.SourcesJson), x.CreatedAt }));
        });

        group.MapPost("/{conversationId:guid}/messages", async (Guid conversationId, SendMessageRequest request, ClaimsPrincipal principal, ConversationService service, CancellationToken ct) =>
            Results.Ok(await service.SendAsync(HttpActorContext.From(principal), conversationId, request.Message, ct)));

        group.MapPost("/{conversationId:guid}/messages/stream", async (Guid conversationId, SendMessageRequest request, ClaimsPrincipal principal, ConversationService service, HttpResponse response, CancellationToken ct) =>
        {
            response.ContentType = "text/event-stream";
            response.Headers.CacheControl = "no-cache";
            response.Headers.Connection = "keep-alive";
            response.Headers["X-Accel-Buffering"] = "no";

            using var writeGate = new SemaphoreSlim(1, 1);
            async Task SendEventAsync(string eventName, object payload, CancellationToken token)
            {
                await writeGate.WaitAsync(token);
                try
                {
                    await response.WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, WebJson)}\n\n", token);
                    await response.Body.FlushAsync(token);
                }
                finally
                {
                    writeGate.Release();
                }
            }

            var result = await service.StreamAsync(
                HttpActorContext.From(principal),
                conversationId,
                request.Message,
                (delta, token) => SendEventAsync("delta", new { text = delta }, token),
                (progress, token) => SendEventAsync("progress", progress, token),
                ct);
            await SendEventAsync("complete", result, ct);
        });

        group.MapDelete("/{conversationId:guid}", async (Guid conversationId, ClaimsPrincipal principal, ConversationService service, CancellationToken ct) =>
        {
            await service.DeleteAsync(HttpActorContext.From(principal), conversationId, ct);
            return Results.NoContent();
        });

        app.MapPost("/api/chat", async (ChatRequest request, ClaimsPrincipal principal, ConversationService service, CancellationToken ct) =>
        {
            var actor = HttpActorContext.From(principal);
            var conversationId = request.ConversationId;
            if (!conversationId.HasValue)
                conversationId = (await service.CreateAsync(actor, new CreateConversationRequest(request.KnowledgeBaseId), ct)).Id;
            return Results.Ok(await service.SendAsync(actor, conversationId.Value, request.Message, ct));
        }).RequireAuthorization().WithTags("Conversations");

        return app;
    }

    private static string? NormalizeSourcesJson(string? sourcesJson)
    {
        if (string.IsNullOrWhiteSpace(sourcesJson)) return sourcesJson;
        try
        {
            var sources = JsonSerializer.Deserialize<List<ChatSource>>(sourcesJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return sources is null ? sourcesJson : JsonSerializer.Serialize(sources, WebJson);
        }
        catch (JsonException)
        {
            return sourcesJson;
        }
    }
}
