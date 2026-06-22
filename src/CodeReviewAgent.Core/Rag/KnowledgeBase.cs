using CodeReviewAgent.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Core.Rag;

/// <summary>
/// 编码规范知识库实现（组员 B 负责）。
///
/// 预期技术路线：
///   InitializeAsync —— 用 <see cref="DirectoryLocator"/> 定位 knowledge/ 目录，读取 .md 语料，
///     按 <see cref="RagOptions.ChunkSize"/>/<see cref="RagOptions.ChunkOverlap"/> 分块，
///     调 <see cref="IEmbeddingService"/> 批量向量化，存入内存向量列表（幂等，只初始化一次）。
///   SearchAsync —— 把 query 向量化，与各分块做余弦相似度，返回 topK 个最相关片段。
/// 规模小，内存向量 + 余弦相似度即可，无需外部向量库（如需加分可接 Qdrant）。
/// </summary>
public sealed class KnowledgeBase : IKnowledgeBase
{
    private readonly IEmbeddingService _embedding;
    private readonly RagOptions _rag;
    private readonly ILogger<KnowledgeBase> _logger;

    public KnowledgeBase(IEmbeddingService embedding, IOptions<AgentConfig> config, ILogger<KnowledgeBase> logger)
    {
        _embedding = embedding;
        _rag = config.Value.Rag;
        _logger = logger;
    }

    public Task InitializeAsync(CancellationToken ct = default)
    {
        // TODO(组员B): 加载 knowledge/ 语料 → 分块 → 向量化 → 建内存索引（幂等）。
        throw new NotImplementedException("KnowledgeBase.InitializeAsync 待组员 B 实现。");
    }

    public Task<IReadOnlyList<string>> SearchAsync(string query, int topK, CancellationToken ct = default)
    {
        // TODO(组员B): query 向量化 → 余弦相似度 → 返回 topK 片段。
        throw new NotImplementedException("KnowledgeBase.SearchAsync 待组员 B 实现。");
    }
}
