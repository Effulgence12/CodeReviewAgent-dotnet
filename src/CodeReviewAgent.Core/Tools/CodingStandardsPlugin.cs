using System.ComponentModel;
using CodeReviewAgent.Core.Rag;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Tools;

/// <summary>
/// 编码规范检索工具（组员 B 负责）：RAG 的对 Agent 出口。
/// 让 Agent 在下结论前先检索权威规范，给意见提供「依据」，而非凭空判断。
/// </summary>
public sealed class CodingStandardsPlugin
{
    private readonly IKnowledgeBase _knowledgeBase;
    private readonly int _topK;

    /// <param name="knowledgeBase">RAG 知识库（由 DI 提供，B 负责实现）。</param>
    public CodingStandardsPlugin(IKnowledgeBase knowledgeBase, int topK = 4)
    {
        _knowledgeBase = knowledgeBase;
        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK));
        }
        _topK = topK;
    }

    [KernelFunction("search_standards")]
    [Description("从编码规范知识库中检索与查询最相关的条款（语义检索）。用于为审查意见提供规范依据。")]
    public async Task<string> SearchStandardsAsync(
        [Description("要查询的规范主题，如 异常处理、命名规范、异步用法")] string query,
        CancellationToken ct = default)
    {
        await _knowledgeBase.InitializeAsync(ct);
        var hits = await _knowledgeBase.SearchAsync(query, _topK, ct);
        return hits.Count == 0
            ? "未检索到相关规范条款。"
            : string.Join("\n\n---\n\n", hits);
    }
}
