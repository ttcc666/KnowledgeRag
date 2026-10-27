using System.Text;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace KnowledgeRag.Api.Extensions;

internal static class AuthenticationExtensions
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();
        if (Encoding.UTF8.GetByteCount(auth.SigningKey) < 32)
            throw new InvalidOperationException("Auth:SigningKey 至少需要 32 字节。");

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey));
        services.AddAuthentication(options =>
            {
                options.DefaultScheme = "SmartAuth";
                options.DefaultAuthenticateScheme = "SmartAuth";
                options.DefaultChallengeScheme = "SmartAuth";
            })
            .AddPolicyScheme("SmartAuth", "JWT or API Key", options =>
            {
                options.ForwardDefaultSelector = context => context.Request.Headers.ContainsKey(ApiKeyAuthenticationHandler.HeaderName)
                    ? ApiKeyAuthenticationHandler.Scheme
                    : JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = auth.Issuer,
                    ValidateAudience = true,
                    ValidAudience = auth.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = signingKey,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            })
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.Scheme, _ => { });

        services.AddAuthorization();
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        return services;
    }
}
