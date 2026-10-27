namespace KnowledgeRag.Application.Contracts;

public sealed record CreateConversationRequest(Guid KnowledgeBaseId, string? Title = null);
public sealed record SendMessageRequest(string Message);
public sealed record ChatRequest(Guid KnowledgeBaseId, string Message, Guid? ConversationId = null);
public sealed record ChatSource(Guid DocumentId, string FileName, int ChunkIndex, string SourceLink, double Score);
public sealed record ChatResponse(Guid ConversationId, string Answer, IReadOnlyList<ChatSource> Sources);
public sealed record AgentTurnResult(string Answer, string SessionJson, IReadOnlyList<SearchHit> Sources);
