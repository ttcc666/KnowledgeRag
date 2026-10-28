using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GovernanceSnapshotSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResourceType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    ResourceId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    DetailsJson = table.Column<string>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationDatasets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationDatasets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationDatasets_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvaluationDatasets_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseRagProfiles",
                columns: table => new
                {
                    KnowledgeBaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EnablePlanner = table.Column<bool>(type: "INTEGER", nullable: false),
                    PlannerMaxQueries = table.Column<int>(type: "INTEGER", nullable: false),
                    MultiQueryTopK = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxMergedEvidence = table.Column<int>(type: "INTEGER", nullable: false),
                    EnableReviewer = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReviewerMaxRetries = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewerMinConfidence = table.Column<double>(type: "REAL", nullable: false),
                    PlannerModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AnswerModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ReviewerModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PromptProfileId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AgentMode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    MonthlyBudgetUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    DailyRunLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseRagProfiles", x => x.KnowledgeBaseId);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseRagProfiles_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ModelPrices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    InputCostPerMillionTokens = table.Column<decimal>(type: "TEXT", nullable: false),
                    OutputCostPerMillionTokens = table.Column<decimal>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ModelPrices_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromptProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannerPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    AnswerPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    ReviewerPrompt = table.Column<string>(type: "TEXT", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromptProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromptProfiles_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromptProfiles_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RagRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConversationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Query = table.Column<string>(type: "TEXT", nullable: false),
                    Answer = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AnswerModel = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AgentMode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourceCount = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedInputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedOutputTokens = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedCostUsd = table.Column<decimal>(type: "TEXT", nullable: false),
                    LatencyMs = table.Column<long>(type: "INTEGER", nullable: false),
                    TraceJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RagRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RagRuns_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RagRuns_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RagRuns_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WebhookSubscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    EventTypes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Secret = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastStatusCode = table.Column<int>(type: "INTEGER", nullable: true),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    LastDeliveredAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WebhookSubscriptions_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationCases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatasetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Query = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedAnswerContains = table.Column<string>(type: "TEXT", nullable: true),
                    RequiresKnowledge = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationCases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationCases_EvaluationDatasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "EvaluationDatasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DatasetId = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CaseCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PassedCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PassRate = table.Column<double>(type: "REAL", nullable: false),
                    GroundednessRate = table.Column<double>(type: "REAL", nullable: false),
                    AverageLatencyMs = table.Column<double>(type: "REAL", nullable: false),
                    AverageSourceCount = table.Column<double>(type: "REAL", nullable: false),
                    ResultsJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EvaluationRuns_EvaluationDatasets_DatasetId",
                        column: x => x.DatasetId,
                        principalTable: "EvaluationDatasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EvaluationRuns_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_TenantId_CreatedAt",
                table: "AuditLogs",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationCases_DatasetId_CreatedAt",
                table: "EvaluationCases",
                columns: new[] { "DatasetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationDatasets_KnowledgeBaseId",
                table: "EvaluationDatasets",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationDatasets_TenantId_UpdatedAt",
                table: "EvaluationDatasets",
                columns: new[] { "TenantId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationRuns_DatasetId_CreatedAt",
                table: "EvaluationRuns",
                columns: new[] { "DatasetId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationRuns_KnowledgeBaseId",
                table: "EvaluationRuns",
                column: "KnowledgeBaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPrices_TenantId_Model",
                table: "ModelPrices",
                columns: new[] { "TenantId", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromptProfiles_KnowledgeBaseId_IsActive",
                table: "PromptProfiles",
                columns: new[] { "KnowledgeBaseId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PromptProfiles_TenantId_Name_Version",
                table: "PromptProfiles",
                columns: new[] { "TenantId", "Name", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_RagRuns_ConversationId",
                table: "RagRuns",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_RagRuns_KnowledgeBaseId_CreatedAt",
                table: "RagRuns",
                columns: new[] { "KnowledgeBaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RagRuns_TenantId_CreatedAt",
                table: "RagRuns",
                columns: new[] { "TenantId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WebhookSubscriptions_TenantId_IsActive",
                table: "WebhookSubscriptions",
                columns: new[] { "TenantId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "EvaluationCases");

            migrationBuilder.DropTable(
                name: "EvaluationRuns");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropTable(
                name: "ModelPrices");

            migrationBuilder.DropTable(
                name: "PromptProfiles");

            migrationBuilder.DropTable(
                name: "RagRuns");

            migrationBuilder.DropTable(
                name: "WebhookSubscriptions");

            migrationBuilder.DropTable(
                name: "EvaluationDatasets");
        }
    }
}
