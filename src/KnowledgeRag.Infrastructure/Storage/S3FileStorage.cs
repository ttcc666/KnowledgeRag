using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using KnowledgeRag.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace KnowledgeRag.Infrastructure.Storage;

/// <summary>Minimal AWS Signature V4 S3 client. Works with AWS S3 and MinIO without an additional SDK dependency.</summary>
public sealed class S3FileStorage : IFileStorage
{
    private readonly HttpClient _http;
    private readonly S3Options _options;
    private readonly string _stagingRoot;

    public S3FileStorage(HttpClient http, IOptions<S3Options> options, IOptions<StorageOptions> storage, IWebHostEnvironment environment)
    {
        _http = http;
        _options = options.Value;
        var configured = storage.Value.RootPath;
        var root = Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
        _stagingRoot = Path.Combine(root, "staging");
        Directory.CreateDirectory(_stagingRoot);
    }

    public async Task<StoredFileResult> SaveAsync(Guid knowledgeBaseId, Guid documentId, IFormFile file, CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var key = $"knowledge-bases/{knowledgeBaseId:N}/{documentId:N}{ext}";
        var temp = Path.Combine(_stagingRoot, $"{Guid.NewGuid():N}{ext}");
        try
        {
            await using (var input = file.OpenReadStream())
            await using (var output = File.Create(temp))
                await input.CopyToAsync(output, cancellationToken);
            var payloadHash = await HashFileAsync(temp, cancellationToken);
            await using var body = File.OpenRead(temp);
            using var content = new StreamContent(body);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType);
            using var request = CreateSignedRequest(HttpMethod.Put, key, payloadHash, content);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"S3 上传失败 ({(int)response.StatusCode}): {await response.Content.ReadAsStringAsync(cancellationToken)}");
            return new StoredFileResult(key, temp, payloadHash, true);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            throw;
        }
    }

    public async Task<Stream?> OpenReadAsync(Guid knowledgeBaseId, string storageKey, CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        using var request = CreateSignedRequest(HttpMethod.Get, storageKey, EmptySha256, null);
        var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) { response.Dispose(); return null; }
        if (!response.IsSuccessStatusCode)
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            throw new InvalidOperationException($"S3 下载失败: {raw}");
        }
        var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return new ResponseOwnedStream(stream, response);
    }

    public async Task DeleteAsync(Guid knowledgeBaseId, string storageKey, CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        using var request = CreateSignedRequest(HttpMethod.Delete, storageKey, EmptySha256, null);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            throw new InvalidOperationException($"S3 删除失败: {await response.Content.ReadAsStringAsync(cancellationToken)}");
    }

    private HttpRequestMessage CreateSignedRequest(HttpMethod method, string key, string payloadHash, HttpContent? content)
    {
        var now = DateTimeOffset.UtcNow;
        var amzDate = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var dateStamp = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var uri = BuildObjectUri(key);
        var canonicalUri = uri.AbsolutePath;
        var host = uri.IsDefaultPort ? uri.Host : uri.Authority;
        var canonicalHeaders = $"host:{host}\nx-amz-content-sha256:{payloadHash}\nx-amz-date:{amzDate}\n";
        const string signedHeaders = "host;x-amz-content-sha256;x-amz-date";
        var canonicalRequest = $"{method.Method}\n{canonicalUri}\n\n{canonicalHeaders}\n{signedHeaders}\n{payloadHash}";
        var scope = $"{dateStamp}/{_options.Region}/s3/aws4_request";
        var stringToSign = $"AWS4-HMAC-SHA256\n{amzDate}\n{scope}\n{Sha256Hex(canonicalRequest)}";
        var signingKey = GetSignatureKey(_options.SecretKey, dateStamp, _options.Region, "s3");
        var signature = Convert.ToHexString(HMACSHA256.HashData(signingKey, Encoding.UTF8.GetBytes(stringToSign))).ToLowerInvariant();

        var request = new HttpRequestMessage(method, uri) { Content = content };
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", payloadHash);
        request.Headers.TryAddWithoutValidation("Authorization", $"AWS4-HMAC-SHA256 Credential={_options.AccessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
        return request;
    }

    private Uri BuildObjectUri(string key)
    {
        var endpoint = new Uri(_options.Endpoint.TrimEnd('/') + "/");
        var encodedKey = string.Join('/', key.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString));
        if (_options.ForcePathStyle)
            return new Uri(endpoint, $"{Uri.EscapeDataString(_options.Bucket)}/{encodedKey}");
        var builder = new UriBuilder(endpoint) { Host = $"{_options.Bucket}.{endpoint.Host}", Path = encodedKey };
        return builder.Uri;
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Endpoint) || string.IsNullOrWhiteSpace(_options.Bucket) || string.IsNullOrWhiteSpace(_options.AccessKey) || string.IsNullOrWhiteSpace(_options.SecretKey))
            throw new InvalidOperationException("S3 配置不完整。");
    }

    private static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct)).ToLowerInvariant();
    }

    private static string Sha256Hex(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static byte[] Hmac(byte[] key, string data) => HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(data));
    private static byte[] GetSignatureKey(string key, string date, string region, string service)
        => Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + key), date), region), service), "aws4_request");
    private const string EmptySha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private sealed class ResponseOwnedStream(Stream inner, HttpResponseMessage response) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin); public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => inner.ReadAsync(buffer, offset, count, cancellationToken);
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); response.Dispose(); } base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); response.Dispose(); GC.SuppressFinalize(this); }
    }
}
