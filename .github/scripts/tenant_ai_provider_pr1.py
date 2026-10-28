from pathlib import Path


def replace_once(path: str, old: str, new: str):
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != 1:
        raise SystemExit(f'{path}: expected exactly one match, got {count}: {old[:80]!r}')
    p.write_text(text.replace(old, new), encoding='utf-8')


def replace_all(path: str, old: str, new: str, expected: int):
    p = Path(path)
    text = p.read_text(encoding='utf-8')
    count = text.count(old)
    if count != expected:
        raise SystemExit(f'{path}: expected {expected} matches, got {count}: {old[:80]!r}')
    p.write_text(text.replace(old, new), encoding='utf-8')

rag = 'src/KnowledgeRag.Infrastructure/Conversations/RagAgentService.cs'
replace_once(
    rag,
    '    RagRuntimeProfileService runtimeProfiles,\n    IOptions<AiOptions> aiOptions,',
    '    RagRuntimeProfileService runtimeProfiles,\n    AiProviderClientFactory aiClientFactory,\n    IOptions<AiOptions> aiOptions,')
replace_once(
    rag,
    '        null,\n        null,\n        null,\n        null);',
    '        null,\n        null,\n        null,\n        null,\n        new ResolvedAiEndpoint(null, null, AiProviderTypes.OpenAiCompatible, _ai.BaseUrl, _ai.ApiKey, _ai.ChatModel, 0, false),\n        null);')
replace_all(
    rag,
    '            Math.Min(profile.MultiQueryTopK, profile.MaxMergedEvidence));',
    '            Math.Min(profile.MultiQueryTopK, profile.MaxMergedEvidence),\n            rerankerEndpoint: profile.RerankerEndpoint);',
    2)
replace_once(
    rag,
    '''    private AIAgent BuildAgent(string name, string model, string instructions, IList<AITool>? tools = null)\n    {\n        var credential = new ApiKeyCredential(string.IsNullOrWhiteSpace(_ai.ApiKey) ? "local-development-key" : _ai.ApiKey);\n        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(_ai.BaseUrl.TrimEnd('/') + "/") };\n        var chatClient = new ChatClient(model, credential, clientOptions);\n        return chatClient.AsAIAgent(new ChatClientAgentOptions\n''',
    '''    private AIAgent BuildAgent(string name, string model, string instructions, IList<AITool>? tools = null)\n    {\n        var chatClient = aiClientFactory.CreateChatClient(Profile.ChatEndpoint, model);\n        return chatClient.AsAIAgent(new ChatClientAgentOptions\n''')

multi = 'src/KnowledgeRag.Infrastructure/Search/MultiQuerySearchService.cs'
replace_once(
    multi,
    '        int? maxMergedEvidence = null,\n        int? finalTopK = null)',
    '        int? maxMergedEvidence = null,\n        int? finalTopK = null,\n        ResolvedAiEndpoint? rerankerEndpoint = null)')
replace_once(
    multi,
    '        var reranked = await reranker.RerankAsync(originalQuery, merged, outputTopK, cancellationToken);',
    '        var reranked = await reranker.RerankAsync(originalQuery, merged, outputTopK, cancellationToken, rerankerEndpoint);')
replace_once(
    multi,
    '        int topK,\n        CancellationToken cancellationToken = default)\n    {\n        if (evidence.Count == 0) return [];',
    '        int topK,\n        CancellationToken cancellationToken = default,\n        ResolvedAiEndpoint? rerankerEndpoint = null)\n    {\n        if (evidence.Count == 0) return [];')
replace_once(
    multi,
    '        var reranked = await reranker.RerankAsync(originalQuery, candidates, Math.Clamp(topK, 1, 40), cancellationToken);',
    '        var reranked = await reranker.RerankAsync(originalQuery, candidates, Math.Clamp(topK, 1, 40), cancellationToken, rerankerEndpoint);')

print('PR1 runtime patches applied')
