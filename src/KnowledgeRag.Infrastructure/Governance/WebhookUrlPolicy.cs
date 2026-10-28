using System.Net;
using System.Net.Sockets;

namespace KnowledgeRag.Infrastructure.Governance;

public sealed class WebhookDestinationBlockedException(string message) : Exception(message);

/// <summary>
/// Central SSRF policy for outbound webhook connections. URL syntax is checked before a
/// request is created and DNS is resolved again inside the HTTP connect callback so a
/// redirect or a late DNS resolution cannot silently route delivery to an internal address.
/// </summary>
public static class WebhookUrlPolicy
{
    public static Uri ValidateUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Webhook URL 必须是有效的 http/https 地址。");

        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("Webhook URL 不允许包含用户凭据。");

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (string.IsNullOrWhiteSpace(host) || IsBlockedHostName(host))
            throw new WebhookDestinationBlockedException("Webhook 目标地址不允许指向本机或内部网络。");

        if (IPAddress.TryParse(host, out var literal) && !IsPublicAddress(literal))
            throw new WebhookDestinationBlockedException("Webhook 目标地址不允许指向本机或内部网络。");

        return uri;
    }

    public static async Task<IPAddress[]> ResolvePublicAddressesAsync(string host, CancellationToken ct)
    {
        if (IsBlockedHostName(host))
            throw new WebhookDestinationBlockedException("Webhook 目标地址不允许指向本机或内部网络。");

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
            addresses = [literal];
        else
            addresses = await Dns.GetHostAddressesAsync(host).WaitAsync(ct);

        if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            throw new WebhookDestinationBlockedException("Webhook DNS 解析到了本机、私网或其他受限地址。");

        return addresses;
    }

    public static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.None) || address.Equals(IPAddress.IPv6None)) return false;

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            if (bytes[0] == 0 || bytes[0] == 10 || bytes[0] == 127) return false;
            if (bytes[0] == 169 && bytes[1] == 254) return false;
            if (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) return false;
            if (bytes[0] == 192 && bytes[1] == 168) return false;
            if (bytes[0] >= 224) return false;
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast) return false;
            // RFC 4193 unique-local fc00::/7.
            if (bytes.Length > 0 && (bytes[0] & 0xFE) == 0xFC) return false;
            return true;
        }

        return false;
    }

    public static bool IsBlocked(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is WebhookDestinationBlockedException) return true;
        return false;
    }

    private static bool IsBlockedHostName(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
           || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
}

public static class WebhookSafeConnection
{
    /// <summary>
    /// Creates the dedicated outbound webhook handler. Ambient system/environment proxies are
    /// deliberately disabled: when a proxy is active, SocketsHttpHandler can surface the proxy
    /// endpoint (commonly 127.0.0.1 for local proxy tools) to ConnectCallback instead of the
    /// webhook destination. That both causes false SSRF blocks and prevents destination DNS
    /// validation/pinning from protecting the actual outbound connection.
    /// </summary>
    public static SocketsHttpHandler CreateHttpHandler()
        => new()
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectCallback = ConnectAsync,
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

    public static async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var addresses = await WebhookUrlPolicy.ResolvePublicAddressesAsync(context.DnsEndPoint.Host, ct);
        Exception? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(address, context.DnsEndPoint.Port, ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastError = ex;
                socket.Dispose();
            }
        }

        throw lastError ?? new HttpRequestException("Webhook 目标地址无法连接。");
    }
}
