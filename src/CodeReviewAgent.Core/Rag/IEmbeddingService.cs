namespace CodeReviewAgent.Core.Rag;

/// <summary>
/// Embedding（向量化）服务契约 —— 第 1 周冻结，供 RAG 子系统使用。
/// 把一批文本转成等长的浮点向量，用于后续余弦相似度检索。
/// </summary>
public interface IEmbeddingService
{
    /// <summary>把一批文本向量化；返回顺序与入参一致。</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>便捷扩展：单条文本向量化。</summary>
public static class EmbeddingServiceExtensions
{
    public static async Task<float[]> EmbedAsync(this IEmbeddingService service, string text, CancellationToken ct = default)
    {
        var result = await service.EmbedAsync(new[] { text }, ct);
        return result[0];
    }
}
