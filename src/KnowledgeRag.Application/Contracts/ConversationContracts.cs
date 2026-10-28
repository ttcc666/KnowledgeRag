namespace KnowledgeRag.Application.Contracts;

public sealed record CreateConversationRequest(Guid KnowledgeBaseId, string? Title = null);
public sealed record RenameConversationRequest(string Title);
public sealed record ConversationFeedbackRequest(string Rating);
public sealed record SendMessageRequest(string Message);
public sealed record ChatRequest(Guid KnowledgeBaseId, string Message, Guid? ConversationId = null);
public sealed record ChatSource(
    Guid DocumentId,
    string FileName,
    int ChunkIndex,
    string SourceLink,
    double Score,
    string Excerpt,
    int? PageNumber = null,
    double? VectorScore = null,
    double? Bm25Score = null,
    double? FusionScore = null,
    double? RerankScore = null);
public sealed record ChatResponse(Guid ConversationId, string Answer, IReadOnlyList<ChatSource> Sources);
public sealed record AgentTurnResult(
    string Answer,
    string SessionJson,
    IReadOnlyList<SearchHit> Sources,
    bool RequiresKnowledge = true);
public sealed record ChatMessageItem(
    Guid Id,
    string Role,
    string Content,
    string? SourcesJson,
    DateTimeOffset CreatedAt);
public sealed record ChatMessagePage(
    IReadOnlyList<ChatMessageItem> Messages,
    bool HasEarlier,
    DateTimeOffset? PreviousCursor);
