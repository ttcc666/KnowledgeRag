using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261028140000_VersionedDocumentIndex")]
public sealed class VersionedDocumentIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "IndexVersion",
            table: "Documents",
            nullable: false,
            defaultValue: 0);

        // Existing indexed documents already have one active chunk/vector generation. Backfill
        // them to generation 1 so deployment does not temporarily hide previously searchable data.
        migrationBuilder.Sql("""
            UPDATE "Documents"
            SET "IndexVersion" = 1
            WHERE EXISTS (
                SELECT 1
                FROM "DocumentChunks" c
                WHERE c."DocumentId" = "Documents"."Id"
            );
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IndexVersion",
            table: "Documents");
    }
}
