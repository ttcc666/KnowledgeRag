using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Storage;

public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _filesRoot;

    public LocalFileStorage(IWebHostEnvironment environment, IOptions<StorageOptions> options)
    {
        var configured = options.Value.RootPath;
        var root = Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        _filesRoot = Path.Combine(root, "files");
        Directory.CreateDirectory(_filesRoot);
    }

    public async Task<StoredFileResult> SaveAsync(Guid knowledgeBaseId, Guid documentId, IFormFile file, CancellationToken cancellationToken = default)
    {
        var dir = Path.Combine(_filesRoot, knowledgeBaseId.ToString("N"));
        Directory.CreateDirectory(dir);
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var storedFileName = documentId.ToString("N") + extension;
        var path = Path.Combine(dir, storedFileName);
        await using (var input = file.OpenReadStream())
        await using (var output = File.Create(path))
            await input.CopyToAsync(output, cancellationToken);
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return new StoredFileResult(storedFileName, path, Convert.ToHexString(hash).ToLowerInvariant(), false);
    }

    public Task<Stream?> OpenReadAsync(Guid knowledgeBaseId, string storageKey, CancellationToken cancellationToken = default)
    {
        var path = GetFilePath(knowledgeBaseId, storageKey);
        Stream? stream = File.Exists(path) ? File.OpenRead(path) : null;
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(Guid knowledgeBaseId, string storageKey, CancellationToken cancellationToken = default)
    {
        var path = GetFilePath(knowledgeBaseId, storageKey);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    public string GetFilePath(Guid knowledgeBaseId, string storedFileName)
        => Path.Combine(_filesRoot, knowledgeBaseId.ToString("N"), storedFileName);
}
