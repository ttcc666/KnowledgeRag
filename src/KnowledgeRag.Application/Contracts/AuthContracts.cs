namespace KnowledgeRag.Application.Contracts;

public sealed record RegisterRequest(string Email, string Password, string? DisplayName = null);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshTokenRequest(string RefreshToken);
public sealed record UserSummary(Guid Id, string Email, string DisplayName);
public sealed record AuthResponse(
    string AccessToken,
    DateTimeOffset ExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshExpiresAt,
    UserSummary User);
