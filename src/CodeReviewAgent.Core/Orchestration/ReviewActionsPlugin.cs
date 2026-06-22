using System.ComponentModel;
using CodeReviewAgent.Core.Abstractions;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Orchestration;

/// <summary>
/// 把「多 Agent 深度审查」编排器包装成顶层对话 Agent 可调用的**粗粒度动作工具**。
///
/// 这正是「agents-as-tools（把子流程当工具暴露）」模式：对话 Agent 像调用普通工具一样
/// 调用这里的方法，但方法内部触发的是一整套多 Agent 工作流。由此，多 Agent 不再是
/// 孤立的另一条入口，而是被顶层对话 Agent「用起来」的一个动作 —— 四大要素组合咬合。
///
/// 每个会话绑定一个审查根目录与一个观察者（构造时传入），因此本类按会话创建，非单例。
/// </summary>
public sealed class ReviewActionsPlugin
{
    private readonly ReviewOrchestrator _orchestrator;
    private readonly string _root;
    private readonly IAgentObserver _observer;

    public ReviewActionsPlugin(ReviewOrchestrator orchestrator, string root, IAgentObserver? observer = null)
    {
        _orchestrator = orchestrator;
        _root = root;
        _observer = observer ?? NullAgentObserver.Instance;
    }

    /// <summary>粗粒度动作：发起整包多专家深度审查并返回汇总报告。</summary>
    [KernelFunction("run_deep_review")]
    [Description("对整个审查目录发起多专家（风格/安全/性能）并行深度审查并汇总，返回 Markdown 报告。用户要求『全面审查/审一遍整个项目』时使用。")]
    public async Task<string> RunDeepReviewAsync(CancellationToken ct = default)
    {
        var result = await _orchestrator.RunDeepReviewAsync(_root, _observer, ct);
        return $"（深度审查完成，用时 {result.Elapsed.TotalSeconds:F1}s，工具调用 {result.TotalToolCalls} 次）\n\n{result.FinalReport}";
    }

    /// <summary>粗粒度动作：根据已确认的问题清单成批修复并验证编译。</summary>
    [KernelFunction("run_remediation")]
    [Description("根据一份已确认的问题清单，对审查目录成批生成修复并验证编译，返回修复报告。用户要求『把这些问题修了』时使用。")]
    public async Task<string> RunRemediationAsync(
        [Description("已确认要修复的问题清单（通常来自此前的审查报告）")] string confirmedFindings,
        CancellationToken ct = default)
    {
        var result = await _orchestrator.RunRemediationAsync(_root, confirmedFindings, _observer, ct);
        return $"（修复完成，用时 {result.Elapsed.TotalSeconds:F1}s，工具调用 {result.ToolCalls} 次）\n\n{result.Report}";
    }
}
