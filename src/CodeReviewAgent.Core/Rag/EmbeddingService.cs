using CodeReviewAgent.Core.Configuration;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Core.Rag;

/// <summary>
/// Embedding 服务实现（组员 B 负责）。
///
/// 预期技术路线：用注入的 <see cref="IHttpClientFactory"/> 调用 OpenAI 兼容的
/// /embeddings 接口，把文本转成向量；端点/模型/Key 从 <see cref="EmbeddingOptions"/> 读取，
/// 留空时回退到 <see cref="LlmOptions"/>（一个 Key 同时驱动对话与 RAG）。
/// </summary>
public sealed class EmbeddingService : IEmbeddingService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly EmbeddingOptions _embedding;
    private readonly LlmOptions _llm;

    public EmbeddingService(IHttpClientFactory httpClientFactory, IOptions<AgentConfig> config)
    {
        _httpClientFactory = httpClientFactory;
        _embedding = config.Value.Embedding;
        _llm = config.Value.Llm;
    }

    // 留空则回退到对话端点/Key。
    private string Endpoint => string.IsNullOrWhiteSpace(_embedding.Endpoint) ? _llm.Endpoint : _embedding.Endpoint;
    private string ApiKey => string.IsNullOrWhiteSpace(_embedding.ApiKey) ? _llm.ApiKey : _embedding.ApiKey;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        // TODO(组员B): 实现真实向量化。
        // 提示：POST {Endpoint}/embeddings，Body { model = _embedding.Model, input = texts }，
        //       Header Authorization: Bearer {ApiKey}，解析 data[].embedding 返回。
        throw new NotImplementedException("EmbeddingService.EmbedAsync 待组员 B 实现。");
    }
}
