using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantAiEmbeddingIndexGenerations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_DocumentId_ChunkIndex",
                table: "DocumentChunks");

            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_KnowledgeBaseId_DocumentId",
                table: "DocumentChunks");

            migrationBuilder.AddColumn<Guid>(
                name: "ActiveIndexGenerationId",
                table: "KnowledgeBases",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<Guid>(
                name: "IndexGenerationId",
                table: "DocumentChunks",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseIndexGenerations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    KnowledgeBaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    EmbeddingDeploymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DeploymentDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProviderConnectionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProviderType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    ModelName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Dimensions = table.Column<int>(type: "INTEGER", nullable: false),
                    ConnectionRevision = table.Column<int>(type: "INTEGER", nullable: false),
                    DeploymentUpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ExpectedDocumentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CompletedDocumentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FailedDocumentCount = table.Column<int>(type: "INTEGER", nullable: false),
                    ManifestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    FailureReason = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseIndexGenerations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseIndexGenerations_AiProviderConnections_ProviderConnectionId",
                        column: x => x.ProviderConnectionId,
                        principalTable: "AiProviderConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseIndexGenerations_KnowledgeBases_KnowledgeBaseId",
                        column: x => x.KnowledgeBaseId,
                        principalTable: "KnowledgeBases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseIndexGenerations_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseIndexGenerations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "KnowledgeBaseIndexGenerationDocuments",
                columns: table => new
                {
                    IndexGenerationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DocumentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourceSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ChunkCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KnowledgeBaseIndexGenerationDocuments", x => new { x.IndexGenerationId, x.DocumentId });
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseIndexGenerationDocuments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_KnowledgeBaseIndexGenerationDocuments_KnowledgeBaseIndexGenerations_IndexGenerationId",
                        column: x => x.IndexGenerationId,
                        principalTable: "KnowledgeBaseIndexGenerations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId_IndexGenerationId_ChunkIndex",
                table: "DocumentChunks",
                columns: new[] { "DocumentId", "IndexGenerationId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_KnowledgeBaseId_DocumentId_IndexGenerationId",
                table: "DocumentChunks",
                columns: new[] { "KnowledgeBaseId", "DocumentId", "IndexGenerationId" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseIndexGenerationDocuments_DocumentId",
                table: "KnowledgeBaseIndexGenerationDocuments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseIndexGenerationDocuments_IndexGenerationId_Status",
                table: "KnowledgeBaseIndexGenerationDocuments",
                columns: new[] { "IndexGenerationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseIndexGenerations_CreatedByUserId",
                table: "KnowledgeBaseIndexGenerations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseIndexGenerations_KnowledgeBaseId_CreatedAt",
                table: "KnowledgeBaseIndexGenerations",
                columns: new[] { "KnowledgeBaseId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseIndexGenerations_KnowledgeBaseId_Status",
                table: "KnowledgeBaseIndexGenerations",
                columns: new[] { "KnowledgeBaseId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseIndexGenerations_ProviderConnectionId",
                table: "KnowledgeBaseIndexGenerations",
                column: "ProviderConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseIndexGenerations_TenantId",
                table: "KnowledgeBaseIndexGenerations",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KnowledgeBaseIndexGenerationDocuments");

            migrationBuilder.DropTable(
                name: "KnowledgeBaseIndexGenerations");

            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_DocumentId_IndexGenerationId_ChunkIndex",
                table: "DocumentChunks");

            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_KnowledgeBaseId_DocumentId_IndexGenerationId",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "ActiveIndexGenerationId",
                table: "KnowledgeBases");

            migrationBuilder.DropColumn(
                name: "IndexGenerationId",
                table: "DocumentChunks");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_DocumentId_ChunkIndex",
                table: "DocumentChunks",
                columns: new[] { "DocumentId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_KnowledgeBaseId_DocumentId",
                table: "DocumentChunks",
                columns: new[] { "KnowledgeBaseId", "DocumentId" });
        }
    }
}
