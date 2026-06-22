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

    /// <param name="knowledgeBase">RAG 知识库（由 DI 提供，B 负责实现）。</param>
    public CodingStandardsPlugin(IKnowledgeBase knowledgeBase)
    {
        _knowledgeBase = knowledgeBase;
    }

    [KernelFunction("search_standards")]
    [Description("从编码规范知识库中检索与查询最相关的条款（语义检索）。用于为审查意见提供规范依据。")]
    public async Task<string> SearchStandardsAsync(
        [Description("要查询的规范主题，如 异常处理、命名规范、异步用法")] string query)
    {
        // TODO(组员B): 调 _knowledgeBase.SearchAsync(query, topK) 并把片段拼成可读文本返回。
        //             默认实现已可用（依赖 KnowledgeBase 落地后即生效），此处给出基本骨架：
        var hits = await _knowledgeBase.SearchAsync(query, topK: 4);
        return hits.Count == 0
            ? "未检索到相关规范条款。"
            : string.Join("\n\n---\n\n", hits);
    }
}
