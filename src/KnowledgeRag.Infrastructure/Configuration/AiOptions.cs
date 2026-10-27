namespace KnowledgeRag.Infrastructure.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public string BaseUrl { get; set; } = "https://api.openai.com/v1";
    public string ApiKey { get; set; } = string.Empty;
    public string ChatModel { get; set; } = "gpt-4o-mini";

    /// <summary>
    /// Optional model overrides for the RAG workflow. Empty values fall back to ChatModel.
    /// Planner and Reviewer are intended for smaller/cheaper models; Answer can use a stronger model.
    /// </summary>
    public string PlannerModel { get; set; } = string.Empty;
    public string AnswerModel { get; set; } = string.Empty;
    public string ReviewerModel { get; set; } = string.Empty;

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public int EmbeddingDimensions { get; set; } = 1536;
    public bool SendEmbeddingDimensions { get; set; } = false;

    /// <summary>
    /// Optional deployment-specific pricing used only for the operations cost estimate.
    /// Keep at zero when the OpenAI-compatible provider has no configured token price.
    /// </summary>
    public decimal ChatInputCostPerMillionTokens { get; set; }
    public decimal ChatOutputCostPerMillionTokens { get; set; }

    public string ResolvePlannerModel() => string.IsNullOrWhiteSpace(PlannerModel) ? ChatModel : PlannerModel;
    public string ResolveAnswerModel() => string.IsNullOrWhiteSpace(AnswerModel) ? ChatModel : AnswerModel;
    public string ResolveReviewerModel() => string.IsNullOrWhiteSpace(ReviewerModel) ? ChatModel : ReviewerModel;
}
