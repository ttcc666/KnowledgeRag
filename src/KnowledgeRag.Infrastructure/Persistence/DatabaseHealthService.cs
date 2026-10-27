using Microsoft.EntityFrameworkCore;

namespace KnowledgeRag.Infrastructure.Persistence;

public sealed class DatabaseHealthService(AppDbContext db)
{
    public Task<bool> CanConnectAsync(CancellationToken cancellationToken = default)
        => db.Database.CanConnectAsync(cancellationToken);
}
