using KnowledgeRag.Infrastructure.Persistence;
using KnowledgeRag.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
public partial class AppDbContextModelSnapshot : ModelSnapshot
{
    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.14");

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
    }
}
