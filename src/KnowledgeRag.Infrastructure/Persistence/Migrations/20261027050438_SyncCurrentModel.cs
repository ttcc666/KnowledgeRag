using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Synchronizes the EF Core model snapshot with the schema already created by
    /// InitialProduction. The original snapshot was incomplete even though the
    /// initial migration already contained these columns and indexes, so applying
    /// the generated diff would attempt to add existing columns on fresh databases.
    /// </summary>
    public partial class SyncCurrentModel : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionally empty. The preceding InitialProduction migration already
            // creates the current schema. This migration establishes the corrected
            // EF Core 10 model snapshot baseline for future migrations.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Snapshot-baseline migration; no database changes to revert.
        }
    }
}
