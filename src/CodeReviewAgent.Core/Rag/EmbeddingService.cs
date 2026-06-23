using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
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

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }
        if (texts.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("待向量化文本不能包含空项。", nameof(texts));
        }
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("未配置 Embedding ApiKey。 ");
        }

        // 部分端点（如 DashScope）限制单批文本条数（默认上限 10），超出会 400。
        // 这里按 BatchSize 切分、分多次请求，再按全局顺序拼回为一个结果数组。
        var batchSize = _embedding.BatchSize;
        if (batchSize <= 0)
        {
            throw new InvalidOperationException("Embedding.BatchSize 必须大于 0。 ");
        }

        var ordered = new float[texts.Count][];
        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize).ToArray();
            var vectors = await EmbedBatchAsync(batch, ct);
            for (var i = 0; i < vectors.Length; i++)
            {
                ordered[offset + i] = vectors[i];
            }
        }

        var dimension = ordered[0].Length;
        if (ordered.Any(vector => vector is null || vector.Length != dimension))
        {
            throw new InvalidDataException("Embedding 响应向量维度不一致。 ");
        }
        return ordered;
    }

    /// <summary>请求单批（条数已不超过端点上限）并按批内 index 排好序返回。</summary>
    private async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> batch, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{Endpoint.TrimEnd('/')}/embeddings");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiKey);
        request.Content = JsonContent.Create(new EmbeddingRequest(_embedding.Model, batch));

        var client = _httpClientFactory.CreateClient(nameof(EmbeddingService));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            // 把响应体附进异常，便于排错（如端点返回的参数/批量上限错误说明），失败读取不掩盖原错误。
            string detail;
            try
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                detail = body.Length <= 500 ? body : body[..500] + "…";
            }
            catch
            {
                detail = "(无法读取响应体)";
            }
            throw new HttpRequestException(
                $"Embedding 请求失败：HTTP {(int)response.StatusCode} ({response.ReasonPhrase})。响应：{detail}");
        }

        EmbeddingResponse payload;
        try
        {
            payload = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(cancellationToken: ct)
                ?? throw new InvalidDataException("Embedding 响应为空。 ");
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidDataException("Embedding 响应不是有效 JSON。 ", ex);
        }

        if (payload.Data is null || payload.Data.Count != batch.Count)
        {
            throw new InvalidDataException("Embedding 响应数量与输入数量不一致。 ");
        }

        var ordered = new float[batch.Count][];
        foreach (var item in payload.Data)
        {
            if (item.Index < 0 || item.Index >= batch.Count || ordered[item.Index] is not null)
            {
                throw new InvalidDataException("Embedding 响应包含无效或重复的 index。 ");
            }
            if (item.Embedding is null || item.Embedding.Length == 0)
            {
                throw new InvalidDataException("Embedding 响应包含空向量。 ");
            }
            ordered[item.Index] = item.Embedding;
        }
        return ordered;
    }

    private sealed record EmbeddingRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("input")] IReadOnlyList<string> Input);

    private sealed record EmbeddingResponse(
        [property: JsonPropertyName("data")] IReadOnlyList<EmbeddingItem>? Data);

    private sealed record EmbeddingItem(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("embedding")] float[]? Embedding);
}
