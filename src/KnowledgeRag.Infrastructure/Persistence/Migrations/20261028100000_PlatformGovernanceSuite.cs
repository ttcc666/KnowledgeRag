using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261028100000_PlatformGovernanceSuite")]
public sealed class PlatformGovernanceSuite : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "KnowledgeBaseRagProfiles",
            columns: table => new
            {
                KnowledgeBaseId = table.Column<Guid>(nullable: false), EnablePlanner = table.Column<bool>(nullable: false), PlannerMaxQueries = table.Column<int>(nullable: false),
                MultiQueryTopK = table.Column<int>(nullable: false), MaxMergedEvidence = table.Column<int>(nullable: false), EnableReviewer = table.Column<bool>(nullable: false),
                ReviewerMaxRetries = table.Column<int>(nullable: false), ReviewerMinConfidence = table.Column<double>(nullable: false), PlannerModel = table.Column<string>(maxLength: 200, nullable: false),
                AnswerModel = table.Column<string>(maxLength: 200, nullable: false), ReviewerModel = table.Column<string>(maxLength: 200, nullable: false), PromptProfileId = table.Column<Guid>(nullable: true),
                AgentMode = table.Column<string>(maxLength: 64, nullable: false), MonthlyBudgetUsd = table.Column<decimal>(nullable: false), DailyRunLimit = table.Column<int>(nullable: false),
                UpdatedByUserId = table.Column<Guid>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KnowledgeBaseRagProfiles", x => x.KnowledgeBaseId);
                table.ForeignKey("FK_KnowledgeBaseRagProfiles_KnowledgeBases_KnowledgeBaseId", x => x.KnowledgeBaseId, "KnowledgeBases", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "RagRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: false), ConversationId = table.Column<Guid>(nullable: true),
                UserId = table.Column<Guid>(nullable: false), ApiKeyId = table.Column<Guid>(nullable: true), Query = table.Column<string>(nullable: false), Answer = table.Column<string>(nullable: false),
                Status = table.Column<string>(maxLength: 32, nullable: false), AnswerModel = table.Column<string>(maxLength: 200, nullable: false), AgentMode = table.Column<string>(maxLength: 64, nullable: false),
                SourceCount = table.Column<int>(nullable: false), EstimatedInputTokens = table.Column<int>(nullable: false), EstimatedOutputTokens = table.Column<int>(nullable: false), EstimatedCostUsd = table.Column<decimal>(nullable: false),
                LatencyMs = table.Column<long>(nullable: false), TraceJson = table.Column<string>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RagRuns", x => x.Id);
                table.ForeignKey("FK_RagRuns_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_RagRuns_KnowledgeBases_KnowledgeBaseId", x => x.KnowledgeBaseId, "KnowledgeBases", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_RagRuns_Conversations_ConversationId", x => x.ConversationId, "Conversations", "Id", onDelete: ReferentialAction.SetNull);
            });
        migrationBuilder.CreateIndex("IX_RagRuns_TenantId_CreatedAt", "RagRuns", new[] { "TenantId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_RagRuns_KnowledgeBaseId_CreatedAt", "RagRuns", new[] { "KnowledgeBaseId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_RagRuns_ConversationId", "RagRuns", "ConversationId");

        migrationBuilder.CreateTable(
            name: "ModelPrices",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), Model = table.Column<string>(maxLength: 200, nullable: false),
                InputCostPerMillionTokens = table.Column<decimal>(nullable: false), OutputCostPerMillionTokens = table.Column<decimal>(nullable: false), IsActive = table.Column<bool>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ModelPrices", x => x.Id);
                table.ForeignKey("FK_ModelPrices_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_ModelPrices_TenantId_Model", "ModelPrices", new[] { "TenantId", "Model" }, unique: true);

        migrationBuilder.CreateTable(
            name: "AuditLogs",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: true), UserId = table.Column<Guid>(nullable: false), ApiKeyId = table.Column<Guid>(nullable: true),
                Action = table.Column<string>(maxLength: 64, nullable: false), ResourceType = table.Column<string>(maxLength: 100, nullable: false), ResourceId = table.Column<string>(maxLength: 200, nullable: true),
                Summary = table.Column<string>(maxLength: 1000, nullable: false), DetailsJson = table.Column<string>(nullable: true), IpAddress = table.Column<string>(maxLength: 128, nullable: true),
                UserAgent = table.Column<string>(maxLength: 512, nullable: true), StatusCode = table.Column<int>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AuditLogs", x => x.Id));
        migrationBuilder.CreateIndex("IX_AuditLogs_TenantId_CreatedAt", "AuditLogs", new[] { "TenantId", "CreatedAt" });

        migrationBuilder.CreateTable(
            name: "PromptProfiles",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: true), Name = table.Column<string>(maxLength: 200, nullable: false),
                Version = table.Column<int>(nullable: false), PlannerPrompt = table.Column<string>(nullable: false), AnswerPrompt = table.Column<string>(nullable: false), ReviewerPrompt = table.Column<string>(nullable: false),
                IsActive = table.Column<bool>(nullable: false), UpdatedByUserId = table.Column<Guid>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PromptProfiles", x => x.Id);
                table.ForeignKey("FK_PromptProfiles_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_PromptProfiles_KnowledgeBases_KnowledgeBaseId", x => x.KnowledgeBaseId, "KnowledgeBases", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_PromptProfiles_TenantId_Name_Version", "PromptProfiles", new[] { "TenantId", "Name", "Version" });
        migrationBuilder.CreateIndex("IX_PromptProfiles_KnowledgeBaseId_IsActive", "PromptProfiles", new[] { "KnowledgeBaseId", "IsActive" });

        migrationBuilder.CreateTable(
            name: "WebhookSubscriptions",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), Name = table.Column<string>(maxLength: 200, nullable: false), Url = table.Column<string>(maxLength: 2000, nullable: false),
                EventTypes = table.Column<string>(maxLength: 1000, nullable: false), Secret = table.Column<string>(maxLength: 1000, nullable: false), IsActive = table.Column<bool>(nullable: false), LastStatusCode = table.Column<int>(nullable: true),
                LastError = table.Column<string>(maxLength: 1000, nullable: true), LastDeliveredAt = table.Column<DateTimeOffset>(nullable: true), CreatedByUserId = table.Column<Guid>(nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                table.ForeignKey("FK_WebhookSubscriptions_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_WebhookSubscriptions_TenantId_IsActive", "WebhookSubscriptions", new[] { "TenantId", "IsActive" });

        migrationBuilder.CreateTable(
            name: "EvaluationDatasets",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: false), Name = table.Column<string>(maxLength: 200, nullable: false),
                Description = table.Column<string>(maxLength: 2000, nullable: true), CreatedByUserId = table.Column<Guid>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationDatasets", x => x.Id);
                table.ForeignKey("FK_EvaluationDatasets_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_EvaluationDatasets_KnowledgeBases_KnowledgeBaseId", x => x.KnowledgeBaseId, "KnowledgeBases", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_EvaluationDatasets_TenantId_UpdatedAt", "EvaluationDatasets", new[] { "TenantId", "UpdatedAt" });
        migrationBuilder.CreateIndex("IX_EvaluationDatasets_KnowledgeBaseId", "EvaluationDatasets", "KnowledgeBaseId");

        migrationBuilder.CreateTable(
            name: "EvaluationCases",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), DatasetId = table.Column<Guid>(nullable: false), Category = table.Column<string>(maxLength: 64, nullable: false), Query = table.Column<string>(nullable: false),
                ExpectedAnswerContains = table.Column<string>(nullable: true), RequiresKnowledge = table.Column<bool>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationCases", x => x.Id);
                table.ForeignKey("FK_EvaluationCases_EvaluationDatasets_DatasetId", x => x.DatasetId, "EvaluationDatasets", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_EvaluationCases_DatasetId_CreatedAt", "EvaluationCases", new[] { "DatasetId", "CreatedAt" });

        migrationBuilder.CreateTable(
            name: "EvaluationRuns",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), DatasetId = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: false), Status = table.Column<string>(maxLength: 32, nullable: false),
                CaseCount = table.Column<int>(nullable: false), PassedCount = table.Column<int>(nullable: false), PassRate = table.Column<double>(nullable: false), GroundednessRate = table.Column<double>(nullable: false),
                AverageLatencyMs = table.Column<double>(nullable: false), AverageSourceCount = table.Column<double>(nullable: false), ResultsJson = table.Column<string>(nullable: false), CreatedByUserId = table.Column<Guid>(nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false), CompletedAt = table.Column<DateTimeOffset>(nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_EvaluationRuns", x => x.Id);
                table.ForeignKey("FK_EvaluationRuns_EvaluationDatasets_DatasetId", x => x.DatasetId, "EvaluationDatasets", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_EvaluationRuns_KnowledgeBases_KnowledgeBaseId", x => x.KnowledgeBaseId, "KnowledgeBases", "Id", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_EvaluationRuns_DatasetId_CreatedAt", "EvaluationRuns", new[] { "DatasetId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_EvaluationRuns_KnowledgeBaseId", "EvaluationRuns", "KnowledgeBaseId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("EvaluationRuns");
        migrationBuilder.DropTable("EvaluationCases");
        migrationBuilder.DropTable("ModelPrices");
        migrationBuilder.DropTable("RagRuns");
        migrationBuilder.DropTable("KnowledgeBaseRagProfiles");
        migrationBuilder.DropTable("WebhookSubscriptions");
        migrationBuilder.DropTable("PromptProfiles");
        migrationBuilder.DropTable("AuditLogs");
        migrationBuilder.DropTable("EvaluationDatasets");
    }
}
