namespace CodeReviewAgent.Core.Rag;

/// <summary>
/// 编码规范知识库契约 —— 第 1 周冻结，RAG 检索的对外接口。
/// 首次使用前 Initialize（读取语料、分块、向量化、建索引），之后按查询返回最相关片段。
/// </summary>
public interface IKnowledgeBase
{
    /// <summary>加载并索引知识库语料（幂等：重复调用应只初始化一次）。</summary>
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>检索与 query 最相关的 topK 个规范片段。</summary>
    Task<IReadOnlyList<string>> SearchAsync(string query, int topK, CancellationToken ct = default);
}
