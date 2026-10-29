using KnowledgeRag.Infrastructure.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KnowledgeRag.Api.IntegrationTests;

public sealed class TypedAiClientRegistrationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public void OpenAiCompatibleEmbeddingService_CanBeResolvedFromTypedHttpClientRegistration()
    {
        using var client = factory.CreateClient();
        var embeddingService = factory.Services.GetRequiredService<OpenAiCompatibleEmbeddingService>();

        Assert.NotNull(embeddingService);
    }
}
