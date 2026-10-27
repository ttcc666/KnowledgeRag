using KnowledgeRag.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("202610270001_InitialProduction")]
public partial class InitialProduction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable("Users", table => new
        {
            Id = table.Column<Guid>(nullable: false), Email = table.Column<string>(maxLength: 320, nullable: false),
            NormalizedEmail = table.Column<string>(maxLength: 320, nullable: false), DisplayName = table.Column<string>(maxLength: 200, nullable: false),
            PasswordHash = table.Column<string>(maxLength: 512, nullable: false), PasswordSalt = table.Column<string>(maxLength: 256, nullable: false),
            IsActive = table.Column<bool>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Users", x => x.Id));

        migrationBuilder.CreateTable("Tenants", table => new
        {
            Id = table.Column<Guid>(nullable: false), Name = table.Column<string>(maxLength: 200, nullable: false), Slug = table.Column<string>(maxLength: 120, nullable: false),
            CreatedByUserId = table.Column<Guid>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Tenants", x => x.Id));

        migrationBuilder.CreateTable("RefreshTokens", table => new
        {
            Id = table.Column<Guid>(nullable: false), UserId = table.Column<Guid>(nullable: false), TokenHash = table.Column<string>(maxLength: 64, nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(nullable: false), ExpiresAt = table.Column<DateTimeOffset>(nullable: false), RevokedAt = table.Column<DateTimeOffset>(nullable: true),
            ReplacedByTokenId = table.Column<Guid>(nullable: true), CreatedByIp = table.Column<string>(maxLength: 128, nullable: true), UserAgent = table.Column<string>(maxLength: 512, nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_RefreshTokens", x => x.Id);
            table.ForeignKey("FK_RefreshTokens_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("TenantMembers", table => new
        {
            TenantId = table.Column<Guid>(nullable: false), UserId = table.Column<Guid>(nullable: false), Role = table.Column<string>(maxLength: 32, nullable: false), AddedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_TenantMembers", x => new { x.TenantId, x.UserId });
            table.ForeignKey("FK_TenantMembers_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_TenantMembers_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("KnowledgeBases", table => new
        {
            Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), Name = table.Column<string>(maxLength: 200, nullable: false),
            Description = table.Column<string>(maxLength: 2000, nullable: true), CreatedByUserId = table.Column<Guid>(nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_KnowledgeBases", x => x.Id);
            table.ForeignKey("FK_KnowledgeBases_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("ApiKeys", table => new
        {
            Id = table.Column<Guid>(nullable: false), TenantId = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: true),
            CreatedByUserId = table.Column<Guid>(nullable: false), Name = table.Column<string>(maxLength: 200, nullable: false), KeyPrefix = table.Column<string>(maxLength: 32, nullable: false),
            KeyHash = table.Column<string>(maxLength: 64, nullable: false), Scopes = table.Column<string>(maxLength: 1000, nullable: false), IsActive = table.Column<bool>(nullable: false),
            CreatedAt = table.Column<DateTimeOffset>(nullable: false), ExpiresAt = table.Column<DateTimeOffset>(nullable: true), LastUsedAt = table.Column<DateTimeOffset>(nullable: true)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ApiKeys", x => x.Id);
            table.ForeignKey("FK_ApiKeys_KnowledgeBases_KnowledgeBaseId", x => x.KnowledgeBaseId, "KnowledgeBases", "Id", onDelete: ReferentialAction.SetNull);
            table.ForeignKey("FK_ApiKeys_Tenants_TenantId", x => x.TenantId, "Tenants", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_ApiKeys_Users_CreatedByUserId", x => x.CreatedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
        });

        migrationBuilder.CreateTable("KnowledgeBaseMembers", table => new
        {
            KnowledgeBaseId = table.Column<Guid>(nullable: false), UserId = table.Column<Guid>(nullable: false), Role = table.Column<string>(maxLength: 32, nullable: false), AddedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_KnowledgeBaseMembers", x => new { x.KnowledgeBaseId, x.UserId });
            table.ForeignKey("FK_KnowledgeBaseMembers_KnowledgeBases_KnowledgeBaseId", x => x.KnowledgeBaseId, "KnowledgeBases", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_KnowledgeBaseMembers_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("Documents", table => new
        {
            Id = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: false), CreatedByUserId = table.Column<Guid>(nullable: false),
            FileName = table.Column<string>(maxLength: 500, nullable: false), ContentType = table.Column<string>(maxLength: 200, nullable: false), StoredFileName = table.Column<string>(maxLength: 1000, nullable: false),
            Sha256 = table.Column<string>(maxLength: 64, nullable: false), SizeBytes = table.Column<long>(nullable: false), ChunkCount = table.Column<int>(nullable: false), Status = table.Column<string>(maxLength: 32, nullable: false),
            LastError = table.Column<string>(maxLength: 4000, nullable: true), CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_Documents", x => x.Id);
            table.ForeignKey("FK_Documents_KnowledgeBases_KnowledgeBaseId", x => x.KnowledgeBaseId, "KnowledgeBases", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("Conversations", table => new
        {
            Id = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: false), UserId = table.Column<Guid>(nullable: false), ApiKeyId = table.Column<Guid>(nullable: true),
            Title = table.Column<string>(maxLength: 300, nullable: false), AgentSessionJson = table.Column<string>(nullable: true), CreatedAt = table.Column<DateTimeOffset>(nullable: false), UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_Conversations", x => x.Id);
            table.ForeignKey("FK_Conversations_ApiKeys_ApiKeyId", x => x.ApiKeyId, "ApiKeys", "Id", onDelete: ReferentialAction.SetNull);
            table.ForeignKey("FK_Conversations_KnowledgeBases_KnowledgeBaseId", x => x.KnowledgeBaseId, "KnowledgeBases", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_Conversations_Users_UserId", x => x.UserId, "Users", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("DocumentChunks", table => new
        {
            Id = table.Column<Guid>(nullable: false), KnowledgeBaseId = table.Column<Guid>(nullable: false), DocumentId = table.Column<Guid>(nullable: false),
            ChunkIndex = table.Column<int>(nullable: false), Text = table.Column<string>(nullable: false), CharacterCount = table.Column<int>(nullable: false), CreatedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_DocumentChunks", x => x.Id);
            table.ForeignKey("FK_DocumentChunks_Documents_DocumentId", x => x.DocumentId, "Documents", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateTable("ChatMessages", table => new
        {
            Id = table.Column<Guid>(nullable: false), ConversationId = table.Column<Guid>(nullable: false), Role = table.Column<string>(maxLength: 32, nullable: false),
            Content = table.Column<string>(nullable: false), SourcesJson = table.Column<string>(nullable: true), CreatedAt = table.Column<DateTimeOffset>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_ChatMessages", x => x.Id);
            table.ForeignKey("FK_ChatMessages_Conversations_ConversationId", x => x.ConversationId, "Conversations", "Id", onDelete: ReferentialAction.Cascade);
        });

        migrationBuilder.CreateIndex("IX_Users_NormalizedEmail", "Users", "NormalizedEmail", unique: true);
        migrationBuilder.CreateIndex("IX_Tenants_Slug", "Tenants", "Slug", unique: true);
        migrationBuilder.CreateIndex("IX_Tenants_CreatedByUserId", "Tenants", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_TenantMembers_UserId", "TenantMembers", "UserId");
        migrationBuilder.CreateIndex("IX_RefreshTokens_TokenHash", "RefreshTokens", "TokenHash", unique: true);
        migrationBuilder.CreateIndex("IX_RefreshTokens_UserId_ExpiresAt", "RefreshTokens", new[] { "UserId", "ExpiresAt" });
        migrationBuilder.CreateIndex("IX_KnowledgeBases_TenantId_UpdatedAt", "KnowledgeBases", new[] { "TenantId", "UpdatedAt" });
        migrationBuilder.CreateIndex("IX_KnowledgeBases_CreatedByUserId", "KnowledgeBases", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_ApiKeys_KeyHash", "ApiKeys", "KeyHash", unique: true);
        migrationBuilder.CreateIndex("IX_ApiKeys_TenantId_IsActive", "ApiKeys", new[] { "TenantId", "IsActive" });
        migrationBuilder.CreateIndex("IX_ApiKeys_KnowledgeBaseId", "ApiKeys", "KnowledgeBaseId");
        migrationBuilder.CreateIndex("IX_ApiKeys_CreatedByUserId", "ApiKeys", "CreatedByUserId");
        migrationBuilder.CreateIndex("IX_KnowledgeBaseMembers_UserId", "KnowledgeBaseMembers", "UserId");
        migrationBuilder.CreateIndex("IX_Documents_KnowledgeBaseId_CreatedAt", "Documents", new[] { "KnowledgeBaseId", "CreatedAt" });
        migrationBuilder.CreateIndex("IX_Documents_KnowledgeBaseId_Sha256", "Documents", new[] { "KnowledgeBaseId", "Sha256" });
        migrationBuilder.CreateIndex("IX_DocumentChunks_KnowledgeBaseId_DocumentId", "DocumentChunks", new[] { "KnowledgeBaseId", "DocumentId" });
        migrationBuilder.CreateIndex("IX_DocumentChunks_DocumentId_ChunkIndex", "DocumentChunks", new[] { "DocumentId", "ChunkIndex" }, unique: true);
        migrationBuilder.CreateIndex("IX_Conversations_UserId_UpdatedAt", "Conversations", new[] { "UserId", "UpdatedAt" });
        migrationBuilder.CreateIndex("IX_Conversations_KnowledgeBaseId_UserId", "Conversations", new[] { "KnowledgeBaseId", "UserId" });
        migrationBuilder.CreateIndex("IX_Conversations_ApiKeyId", "Conversations", "ApiKeyId");
        migrationBuilder.CreateIndex("IX_ChatMessages_ConversationId_CreatedAt", "ChatMessages", new[] { "ConversationId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ChatMessages");
        migrationBuilder.DropTable("DocumentChunks");
        migrationBuilder.DropTable("Conversations");
        migrationBuilder.DropTable("Documents");
        migrationBuilder.DropTable("ApiKeys");
        migrationBuilder.DropTable("KnowledgeBaseMembers");
        migrationBuilder.DropTable("RefreshTokens");
        migrationBuilder.DropTable("KnowledgeBases");
        migrationBuilder.DropTable("TenantMembers");
        migrationBuilder.DropTable("Tenants");
        migrationBuilder.DropTable("Users");
    }
}
