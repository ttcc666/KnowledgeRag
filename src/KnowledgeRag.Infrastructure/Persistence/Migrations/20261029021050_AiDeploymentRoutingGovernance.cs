using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiDeploymentRoutingGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var guidColumnType = ActiveProvider.Contains("Npgsql", StringComparison.Ordinal) ? "uuid" : "TEXT";

            migrationBuilder.DropIndex(
                name: "IX_ModelPrices_TenantId_Model",
                table: "ModelPrices");

            migrationBuilder.AddColumn<Guid>(
                name: "AnswerDeploymentId",
                table: "RagRuns",
                type: guidColumnType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeploymentId",
                table: "ModelPrices",
                type: guidColumnType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AnswerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                type: guidColumnType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PlannerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                type: guidColumnType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RerankerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                type: guidColumnType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                type: guidColumnType,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "FallbackDeploymentId",
                table: "AiModelDeployments",
                type: guidColumnType,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HealthStatus",
                table: "AiModelDeployments",
                type: null,
                maxLength: 32,
                nullable: false,
                defaultValue: "unknown");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastHealthCheckedAt",
                table: "AiModelDeployments",
                type: null,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastHealthError",
                table: "AiModelDeployments",
                type: null,
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RagRuns_AnswerDeploymentId",
                table: "RagRuns",
                column: "AnswerDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPrices_DeploymentId",
                table: "ModelPrices",
                column: "DeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPrices_TenantId_DeploymentId",
                table: "ModelPrices",
                columns: new[] { "TenantId", "DeploymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ModelPrices_TenantId_Model",
                table: "ModelPrices",
                columns: new[] { "TenantId", "Model" });

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseRagProfiles_AnswerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                column: "AnswerDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseRagProfiles_PlannerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                column: "PlannerDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseRagProfiles_RerankerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                column: "RerankerDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_KnowledgeBaseRagProfiles_ReviewerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                column: "ReviewerDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiModelDeployments_FallbackDeploymentId",
                table: "AiModelDeployments",
                column: "FallbackDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_AiModelDeployments_TenantId_HealthStatus",
                table: "AiModelDeployments",
                columns: new[] { "TenantId", "HealthStatus" });

            migrationBuilder.AddForeignKey(
                name: "FK_AiModelDeployments_AiModelDeployments_FallbackDeploymentId",
                table: "AiModelDeployments",
                column: "FallbackDeploymentId",
                principalTable: "AiModelDeployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeBaseRagProfiles_AiModelDeployments_AnswerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                column: "AnswerDeploymentId",
                principalTable: "AiModelDeployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeBaseRagProfiles_AiModelDeployments_PlannerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                column: "PlannerDeploymentId",
                principalTable: "AiModelDeployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeBaseRagProfiles_AiModelDeployments_RerankerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                column: "RerankerDeploymentId",
                principalTable: "AiModelDeployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_KnowledgeBaseRagProfiles_AiModelDeployments_ReviewerDeploymentId",
                table: "KnowledgeBaseRagProfiles",
                column: "ReviewerDeploymentId",
                principalTable: "AiModelDeployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ModelPrices_AiModelDeployments_DeploymentId",
                table: "ModelPrices",
                column: "DeploymentId",
                principalTable: "AiModelDeployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RagRuns_AiModelDeployments_AnswerDeploymentId",
                table: "RagRuns",
                column: "AnswerDeploymentId",
                principalTable: "AiModelDeployments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AiModelDeployments_AiModelDeployments_FallbackDeploymentId",
                table: "AiModelDeployments");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeBaseRagProfiles_AiModelDeployments_AnswerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeBaseRagProfiles_AiModelDeployments_PlannerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeBaseRagProfiles_AiModelDeployments_RerankerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_KnowledgeBaseRagProfiles_AiModelDeployments_ReviewerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropForeignKey(
                name: "FK_ModelPrices_AiModelDeployments_DeploymentId",
                table: "ModelPrices");

            migrationBuilder.DropForeignKey(
                name: "FK_RagRuns_AiModelDeployments_AnswerDeploymentId",
                table: "RagRuns");

            migrationBuilder.DropIndex(
                name: "IX_RagRuns_AnswerDeploymentId",
                table: "RagRuns");

            migrationBuilder.DropIndex(
                name: "IX_ModelPrices_DeploymentId",
                table: "ModelPrices");

            migrationBuilder.DropIndex(
                name: "IX_ModelPrices_TenantId_DeploymentId",
                table: "ModelPrices");

            migrationBuilder.DropIndex(
                name: "IX_ModelPrices_TenantId_Model",
                table: "ModelPrices");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseRagProfiles_AnswerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseRagProfiles_PlannerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseRagProfiles_RerankerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropIndex(
                name: "IX_KnowledgeBaseRagProfiles_ReviewerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropIndex(
                name: "IX_AiModelDeployments_FallbackDeploymentId",
                table: "AiModelDeployments");

            migrationBuilder.DropIndex(
                name: "IX_AiModelDeployments_TenantId_HealthStatus",
                table: "AiModelDeployments");

            migrationBuilder.DropColumn(
                name: "AnswerDeploymentId",
                table: "RagRuns");

            migrationBuilder.DropColumn(
                name: "DeploymentId",
                table: "ModelPrices");

            migrationBuilder.DropColumn(
                name: "AnswerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropColumn(
                name: "PlannerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropColumn(
                name: "RerankerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropColumn(
                name: "ReviewerDeploymentId",
                table: "KnowledgeBaseRagProfiles");

            migrationBuilder.DropColumn(
                name: "FallbackDeploymentId",
                table: "AiModelDeployments");

            migrationBuilder.DropColumn(
                name: "HealthStatus",
                table: "AiModelDeployments");

            migrationBuilder.DropColumn(
                name: "LastHealthCheckedAt",
                table: "AiModelDeployments");

            migrationBuilder.DropColumn(
                name: "LastHealthError",
                table: "AiModelDeployments");

            migrationBuilder.CreateIndex(
                name: "IX_ModelPrices_TenantId_Model",
                table: "ModelPrices",
                columns: new[] { "TenantId", "Model" },
                unique: true);
        }
    }
}
