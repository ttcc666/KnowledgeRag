using Microsoft.AspNetCore.Http;
namespace KnowledgeRag.Infrastructure.Storage;

public sealed record StoredFileResult(string StorageKey, string ParseFilePath, string Sha256, bool DeleteParseFileWhenDone);

public interface IFileStorage
{
    Task<StoredFileResult> SaveAsync(Guid knowledgeBaseId, Guid documentId, IFormFile file, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(Guid knowledgeBaseId, string storageKey, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid knowledgeBaseId, string storageKey, CancellationToken cancellationToken = default);
}
