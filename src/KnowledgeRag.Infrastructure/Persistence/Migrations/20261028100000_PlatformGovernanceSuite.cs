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
            name: "KnowledgeBaseRagProfileEntity",
            columns: table => new
            {
                KnowledgeBaseId = table.Column<Guid>(nullable: false),
                EnablePlanner = table.Column<bool>(nullable: false),
                PlannerMaxQueries = table.Column<int>(nullable: false),
                MultiQueryTopK = table.Column<int>(nullable: false),
                MaxMergedEvidence = table.Column<int>(nullable: false),
                EnableReviewer = table.Column<bool>(nullable: false),
                ReviewerMaxRetries = table.Column<int>(nullable: false),
                ReviewerMinConfidence = table.Column<double>(nullable: false),
                PlannerModel = table.Column<string>(maxLength: 200, nullable: false),
                AnswerModel = table.Column<string>(maxLength: 200, nullable: false),
                ReviewerModel = table.Column<string>(maxLength: 200, nullable: false),
                PromptProfileId = table.Column<Guid>(nullable: true),
                AgentMode = table.Column<string>(maxLength: 64, nullable: false),
                MonthlyBudgetUsd = table.Column<decimal>(nullable: false),
                DailyRunLimit = table.Column<int>(nullable: false),
                UpdatedByUserId = table.Column<Guid>(nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_KnowledgeBaseRagProfileEntity", x => x.KnowledgeBaseId));

        migrationBuilder.CreateTable(
            name: "RagRunEntity",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: false),
                ConversationId = table.Column<Guid>(nullable: true), UserId = table.Column<Guid>(nullable: false), ApiKeyId = table.Column<Guid>(nullable: true),
                Query = table.Column<string>(nullable: false), Answer = table.Column<string>(nullable: false), Status = table.Column<string>(maxLength: 32, nullable: false),
                AnswerModel = table.Column<string>(maxLength: 200, nullable: false), AgentMode = table.Column<string>(maxLength: 64, nullable: false),
                SourceCount = table.Column<int>(nullable: false), EstimatedInputTokens = table.Column<int>(nullable: false), EstimatedOutputTokens = table.Column<int>(nullable: false),
                EstimatedCostUsd = table.Column<decimal>(nullable: false), LatencyMs = table.Column<long>(nullable: false), TraceJson = table.Column<string>(nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_RagRunEntity", x => x.Id));
        migrationBuilder.CreateIndex("IX_RagRunEntity_TenantId_CreatedAt", "RagRunEntity", new[] { "TenantId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_RagRunEntity_KnowledgeBaseId_CreatedAt", "RagRunEntity", new[] { "KnowledgeBaseId", "CreatedAt" });

        migrationBuilder.CreateTable(
            name: "ModelPriceEntity",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), Model = table.Column<string>(maxLength: 200, nullable: false),
                InputCostPerMillionTokens = table.Column<decimal>(nullable: false), OutputCostPerMillionTokens = table.Column<decimal>(nullable: false),
                IsActive = table.Column<bool>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ModelPriceEntity", x => x.Id));
        migrationBuilder.CreateIndex("IX_ModelPriceEntity_TenantId_Model", "ModelPriceEntity", new[] { "TenantId", "Model" }, unique: true);

        migrationBuilder.CreateTable(
            name: "AuditLogEntity",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: true), UserId = table.Column<Guid>(nullable: false), ApiKeyId = table.Column<Guid>(nullable: true),
                Action = table.Column<string>(maxLength: 64, nullable: false), ResourceType = table.Column<string>(maxLength: 100, nullable: false), ResourceId = table.Column<string>(maxLength: 200, nullable: true),
                Summary = table.Column<string>(maxLength: 1000, nullable: false), DetailsJson = table.Column<string>(nullable: true), IpAddress = table.Column<string>(maxLength: 128, nullable: true),
                UserAgent = table.Column<string>(maxLength: 512, nullable: true), StatusCode = table.Column<int>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_AuditLogEntity", x => x.Id));
        migrationBuilder.CreateIndex("IX_AuditLogEntity_TenantId_CreatedAt", "AuditLogEntity", new[] { "TenantId", "CreatedAt" });

        migrationBuilder.CreateTable(
            name: "PromptProfileEntity",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: true), Name = table.Column<string>(maxLength: 200, nullable: false),
                Version = table.Column<int>(nullable: false), PlannerPrompt = table.Column<string>(nullable: false), AnswerPrompt = table.Column<string>(nullable: false), ReviewerPrompt = table.Column<string>(nullable: false),
                IsActive = table.Column<bool>(nullable: false), UpdatedByUserId = table.Column<Guid>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_PromptProfileEntity", x => x.Id));
        migrationBuilder.CreateIndex("IX_PromptProfileEntity_TenantId_Name_Version", "PromptProfileEntity", new[] { "TenantId", "Name", "Version" });
        migrationBuilder.CreateIndex("IX_PromptProfileEntity_KnowledgeBaseId_IsActive", "PromptProfileEntity", new[] { "KnowledgeBaseId", "IsActive" });

        migrationBuilder.CreateTable(
            name: "WebhookSubscriptionEntity",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), Name = table.Column<string>(maxLength: 200, nullable: false), Url = table.Column<string>(maxLength: 2000, nullable: false),
                EventTypes = table.Column<string>(maxLength: 1000, nullable: false), Secret = table.Column<string>(maxLength: 1000, nullable: false), IsActive = table.Column<bool>(nullable: false),
                LastStatusCode = table.Column<int>(nullable: true), LastError = table.Column<string>(maxLength: 1000, nullable: true), LastDeliveredAt = table.Column<DateTimeOffset>(nullable: true),
                CreatedByUserId = table.Column<Guid>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_WebhookSubscriptionEntity", x => x.Id));
        migrationBuilder.CreateIndex("IX_WebhookSubscriptionEntity_TenantId_IsActive", "WebhookSubscriptionEntity", new[] { "TenantId", "IsActive" });

        migrationBuilder.CreateTable(
            name: "EvaluationDatasetEntity",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: false), Name = table.Column<string>(maxLength: 200, nullable: false),
                Description = table.Column<string>(maxLength: 2000, nullable: true), CreatedByUserId = table.Column<Guid>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_EvaluationDatasetEntity", x => x.Id));
        migrationBuilder.CreateIndex("IX_EvaluationDatasetEntity_TenantId_UpdatedAt", "EvaluationDatasetEntity", new[] { "TenantId", "UpdatedAt" });

        migrationBuilder.CreateTable(
            name: "EvaluationCaseEntity",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), DatasetId = table.Column<Guid>(nullable: false), Category = table.Column<string>(maxLength: 64, nullable: false), Query = table.Column<string>(nullable: false),
                ExpectedAnswerContains = table.Column<string>(nullable: true), RequiresKnowledge = table.Column<bool>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_EvaluationCaseEntity", x => x.Id));
        migrationBuilder.CreateIndex("IX_EvaluationCaseEntity_DatasetId_CreatedAt", "EvaluationCaseEntity", new[] { "DatasetId", "CreatedAt" });

        migrationBuilder.CreateTable(
            name: "EvaluationRunEntity",
            columns: table => new
            {
                Id = table.Column<Guid>(nullable: false), DatasetId = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: false), Status = table.Column<string>(maxLength: 32, nullable: false),
                CaseCount = table.Column<int>(nullable: false), PassedCount = table.Column<int>(nullable: false), PassRate = table.Column<double>(nullable: false), GroundednessRate = table.Column<double>(nullable: false),
                AverageLatencyMs = table.Column<double>(nullable: false), AverageSourceCount = table.Column<double>(nullable: false), ResultsJson = table.Column<string>(nullable: false), CreatedByUserId = table.Column<Guid>(nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(nullable: false), CompletedAt = table.Column<DateTimeOffset>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_EvaluationRunEntity", x => x.Id));
        migrationBuilder.CreateIndex("IX_EvaluationRunEntity_DatasetId_CreatedAt", "EvaluationRunEntity", new[] { "DatasetId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("EvaluationRunEntity");
        migrationBuilder.DropTable("EvaluationCaseEntity");
        migrationBuilder.DropTable("EvaluationDatasetEntity");
        migrationBuilder.DropTable("WebhookSubscriptionEntity");
        migrationBuilder.DropTable("PromptProfileEntity");
        migrationBuilder.DropTable("AuditLogEntity");
        migrationBuilder.DropTable("ModelPriceEntity");
        migrationBuilder.DropTable("RagRunEntity");
        migrationBuilder.DropTable("KnowledgeBaseRagProfileEntity");
    }
}
