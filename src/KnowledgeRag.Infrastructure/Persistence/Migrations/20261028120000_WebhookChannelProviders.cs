using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KnowledgeRag.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261028120000_WebhookChannelProviders")]
public sealed class WebhookChannelProviders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Provider",
            table: "WebhookSubscriptions",
            type: null,
            maxLength: 50,
            nullable: false,
            defaultValue: "custom");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Provider", table: "WebhookSubscriptions");
    }
}
