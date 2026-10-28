using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantAiProviderFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiProviderConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProviderType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    EncryptedCredential = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    ConfigJson = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiProviderConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiProviderConnections_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiProviderConnections_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AiModelDeployments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ConnectionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    RemoteModelName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModelType = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    EmbeddingDimensions = table.Column<int>(type: "INTEGER", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiModelDeployments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiModelDeployments_AiProviderConnections_ConnectionId",
                        column: x => x.ConnectionId,
                        principalTable: "AiProviderConnections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AiModelDeployments_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TenantAiDefaults",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DefaultChatDeploymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DefaultEmbeddingDeploymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    DefaultRerankerDeploymentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    UpdatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantAiDefaults", x => x.TenantId);
                    table.ForeignKey(
                        name: "FK_TenantAiDefaults_AiModelDeployments_DefaultChatDeploymentId",
                        column: x => x.DefaultChatDeploymentId,
                        principalTable: "AiModelDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TenantAiDefaults_AiModelDeployments_DefaultEmbeddingDeploymentId",
                        column: x => x.DefaultEmbeddingDeploymentId,
                        principalTable: "AiModelDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TenantAiDefaults_AiModelDeployments_DefaultRerankerDeploymentId",
                        column: x => x.DefaultRerankerDeploymentId,
                        principalTable: "AiModelDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TenantAiDefaults_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TenantAiDefaults_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiModelDeployments_ConnectionId",
                table: "AiModelDeployments",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_AiModelDeployments_TenantId_ConnectionId_DisplayName",
                table: "AiModelDeployments",
                columns: new[] { "TenantId", "ConnectionId", "DisplayName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AiModelDeployments_TenantId_ModelType_IsEnabled",
                table: "AiModelDeployments",
                columns: new[] { "TenantId", "ModelType", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConnections_CreatedByUserId",
                table: "AiProviderConnections",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConnections_TenantId_IsEnabled",
                table: "AiProviderConnections",
                columns: new[] { "TenantId", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_AiProviderConnections_TenantId_Name",
                table: "AiProviderConnections",
                columns: new[] { "TenantId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TenantAiDefaults_DefaultChatDeploymentId",
                table: "TenantAiDefaults",
                column: "DefaultChatDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAiDefaults_DefaultEmbeddingDeploymentId",
                table: "TenantAiDefaults",
                column: "DefaultEmbeddingDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAiDefaults_DefaultRerankerDeploymentId",
                table: "TenantAiDefaults",
                column: "DefaultRerankerDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantAiDefaults_UpdatedByUserId",
                table: "TenantAiDefaults",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TenantAiDefaults");

            migrationBuilder.DropTable(
                name: "AiModelDeployments");

            migrationBuilder.DropTable(
                name: "AiProviderConnections");
        }
    }
}
