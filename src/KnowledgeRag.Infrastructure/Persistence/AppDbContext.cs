using KnowledgeRag.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<TenantEntity> Tenants => Set<TenantEntity>();
    public DbSet<TenantMemberEntity> TenantMembers => Set<TenantMemberEntity>();
    public DbSet<RefreshTokenEntity> RefreshTokens => Set<RefreshTokenEntity>();
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();
    public DbSet<KnowledgeBaseEntity> KnowledgeBases => Set<KnowledgeBaseEntity>();
    public DbSet<KnowledgeBaseMemberEntity> KnowledgeBaseMembers => Set<KnowledgeBaseMemberEntity>();
    public DbSet<DocumentEntity> Documents => Set<DocumentEntity>();
    public DbSet<DocumentChunkEntity> DocumentChunks => Set<DocumentChunkEntity>();
    public DbSet<ConversationEntity> Conversations => Set<ConversationEntity>();
    public DbSet<ChatMessageEntity> ChatMessages => Set<ChatMessageEntity>();
    public DbSet<KnowledgeBaseRagProfileEntity> KnowledgeBaseRagProfiles => Set<KnowledgeBaseRagProfileEntity>();
    public DbSet<RagRunEntity> RagRuns => Set<RagRunEntity>();
    public DbSet<ModelPriceEntity> ModelPrices => Set<ModelPriceEntity>();
    public DbSet<AuditLogEntity> AuditLogs => Set<AuditLogEntity>();
    public DbSet<PromptProfileEntity> PromptProfiles => Set<PromptProfileEntity>();
    public DbSet<WebhookSubscriptionEntity> WebhookSubscriptions => Set<WebhookSubscriptionEntity>();
    public DbSet<EvaluationDatasetEntity> EvaluationDatasets => Set<EvaluationDatasetEntity>();
    public DbSet<EvaluationCaseEntity> EvaluationCases => Set<EvaluationCaseEntity>();
    public DbSet<EvaluationRunEntity> EvaluationRuns => Set<EvaluationRunEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.NormalizedEmail).HasMaxLength(320);
            entity.Property(x => x.DisplayName).HasMaxLength(200);
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
            entity.Property(x => x.PasswordSalt).HasMaxLength(256);
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<TenantEntity>(entity =>
        {
            entity.ToTable("Tenants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Slug).HasMaxLength(120);
            entity.HasIndex(x => x.Slug).IsUnique();
            entity.HasIndex(x => x.CreatedByUserId);
        });

        modelBuilder.Entity<TenantMemberEntity>(entity =>
        {
            entity.ToTable("TenantMembers");
            entity.HasKey(x => new { x.TenantId, x.UserId });
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.UserId);
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshTokenEntity>(entity =>
        {
            entity.ToTable("RefreshTokens");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.TokenHash).HasMaxLength(64);
            entity.Property(x => x.CreatedByIp).HasMaxLength(128);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.HasIndex(x => x.TokenHash).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.ExpiresAt });
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ApiKeyEntity>(entity =>
        {
            entity.ToTable("ApiKeys");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.KeyPrefix).HasMaxLength(32);
            entity.Property(x => x.KeyHash).HasMaxLength(64);
            entity.Property(x => x.Scopes).HasMaxLength(1000);
            entity.HasIndex(x => x.KeyHash).IsUnique();
            entity.HasIndex(x => new { x.TenantId, x.IsActive });
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<KnowledgeBaseEntity>(entity =>
        {
            entity.ToTable("KnowledgeBases");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.HasIndex(x => new { x.TenantId, x.UpdatedAt });
            entity.HasIndex(x => x.CreatedByUserId);
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeBaseMemberEntity>(entity =>
        {
            entity.ToTable("KnowledgeBaseMembers");
            entity.HasKey(x => new { x.KnowledgeBaseId, x.UserId });
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => x.UserId);
            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentEntity>(entity =>
        {
            entity.ToTable("Documents");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FileName).HasMaxLength(500);
            entity.Property(x => x.ContentType).HasMaxLength(200);
            entity.Property(x => x.StoredFileName).HasMaxLength(1000);
            entity.Property(x => x.Sha256).HasMaxLength(64);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            entity.Property(x => x.LastError).HasMaxLength(4000);
            entity.HasIndex(x => new { x.KnowledgeBaseId, x.CreatedAt });
            entity.HasIndex(x => new { x.KnowledgeBaseId, x.Sha256 });
            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DocumentChunkEntity>(entity =>
        {
            entity.ToTable("DocumentChunks");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Text).IsRequired();
            entity.HasIndex(x => new { x.KnowledgeBaseId, x.DocumentId });
            entity.HasIndex(x => new { x.DocumentId, x.ChunkIndex }).IsUnique();
            entity.HasOne<DocumentEntity>().WithMany().HasForeignKey(x => x.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ConversationEntity>(entity =>
        {
            entity.ToTable("Conversations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(300);
            entity.HasIndex(x => new { x.UserId, x.UpdatedAt });
            entity.HasIndex(x => new { x.KnowledgeBaseId, x.UserId });
            entity.HasIndex(x => x.ApiKeyId);
            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<UserEntity>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ApiKeyEntity>().WithMany().HasForeignKey(x => x.ApiKeyId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ChatMessageEntity>(entity =>
        {
            entity.ToTable("ChatMessages");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Role).HasMaxLength(32);
            entity.HasIndex(x => new { x.ConversationId, x.CreatedAt });
            entity.HasOne<ConversationEntity>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<KnowledgeBaseRagProfileEntity>(entity =>
        {
            entity.ToTable("KnowledgeBaseRagProfiles");
            entity.HasKey(x => x.KnowledgeBaseId);
            entity.Property(x => x.PlannerModel).HasMaxLength(200);
            entity.Property(x => x.AnswerModel).HasMaxLength(200);
            entity.Property(x => x.ReviewerModel).HasMaxLength(200);
            entity.Property(x => x.AgentMode).HasMaxLength(64);
            entity.HasOne<KnowledgeBaseEntity>().WithOne().HasForeignKey<KnowledgeBaseRagProfileEntity>(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RagRunEntity>(entity =>
        {
            entity.ToTable("RagRuns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.Property(x => x.AnswerModel).HasMaxLength(200);
            entity.Property(x => x.AgentMode).HasMaxLength(64);
            entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
            entity.HasIndex(x => new { x.KnowledgeBaseId, x.CreatedAt });
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<ConversationEntity>().WithMany().HasForeignKey(x => x.ConversationId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ModelPriceEntity>(entity =>
        {
            entity.ToTable("ModelPrices");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Model).HasMaxLength(200);
            entity.HasIndex(x => new { x.TenantId, x.Model }).IsUnique();
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLogEntity>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Action).HasMaxLength(64);
            entity.Property(x => x.ResourceType).HasMaxLength(100);
            entity.Property(x => x.ResourceId).HasMaxLength(200);
            entity.Property(x => x.Summary).HasMaxLength(1000);
            entity.Property(x => x.IpAddress).HasMaxLength(128);
            entity.Property(x => x.UserAgent).HasMaxLength(512);
            entity.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        modelBuilder.Entity<PromptProfileEntity>(entity =>
        {
            entity.ToTable("PromptProfiles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.HasIndex(x => new { x.TenantId, x.Name, x.Version });
            entity.HasIndex(x => new { x.KnowledgeBaseId, x.IsActive });
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WebhookSubscriptionEntity>(entity =>
        {
            entity.ToTable("WebhookSubscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Url).HasMaxLength(2000);
            entity.Property(x => x.EventTypes).HasMaxLength(1000);
            entity.Property(x => x.Secret).HasMaxLength(1000);
            entity.Property(x => x.LastError).HasMaxLength(1000);
            entity.HasIndex(x => new { x.TenantId, x.IsActive });
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EvaluationDatasetEntity>(entity =>
        {
            entity.ToTable("EvaluationDatasets");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Description).HasMaxLength(2000);
            entity.HasIndex(x => new { x.TenantId, x.UpdatedAt });
            entity.HasOne<TenantEntity>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EvaluationCaseEntity>(entity =>
        {
            entity.ToTable("EvaluationCases");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Category).HasMaxLength(64);
            entity.HasIndex(x => new { x.DatasetId, x.CreatedAt });
            entity.HasOne<EvaluationDatasetEntity>().WithMany().HasForeignKey(x => x.DatasetId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EvaluationRunEntity>(entity =>
        {
            entity.ToTable("EvaluationRuns");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Status).HasMaxLength(32);
            entity.HasIndex(x => new { x.DatasetId, x.CreatedAt });
            entity.HasOne<EvaluationDatasetEntity>().WithMany().HasForeignKey(x => x.DatasetId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<KnowledgeBaseEntity>().WithMany().HasForeignKey(x => x.KnowledgeBaseId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
