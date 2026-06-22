using System.Diagnostics;
using System.Text;
using CodeReviewAgent.Core.Abstractions;
using CodeReviewAgent.Core.Agent;
using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Llm;
using CodeReviewAgent.Core.Memory;
using CodeReviewAgent.Core.Rag;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Core.Orchestration;

/// <summary>
/// 多 Agent 深度审查编排器（固定工作流，被顶层对话 Agent 当作「动作」触发）。
///
/// 三阶段：
///   阶段1 评审：风格 / 安全 / 性能三位专家**并行**（<see cref="Task.WhenAll"/>），
///              每位都是独立的真实 ReAct 循环，自行调用只读工具收集证据并给出本视角意见。
///   阶段2 汇总：主审 Agent 把三份意见合并、去重、按严重级别排序，产出最终报告。
///   阶段3 修复（可选）：修复 Agent 取已确认发现，调用 propose_fix 生成补丁、compile_check 验证。
///
/// 为什么底层用「固定工作流」而非 LLM 动态规划：做全面审查时三视角恒定、结构已知，
/// 固定编排更可预测、可解释、低成本（动态规划留给顶层对话 Agent 决定「要不要触发」）。
/// </summary>
public sealed class ReviewOrchestrator
{
    private readonly KernelFactory _kernelFactory;
    private readonly PluginCatalog _plugins;
    private readonly IKnowledgeBase _knowledgeBase;
    private readonly AgentConfig _config;
    private readonly ILogger<ReviewOrchestrator> _logger;

    public ReviewOrchestrator(
        KernelFactory kernelFactory,
        PluginCatalog plugins,
        IKnowledgeBase knowledgeBase,
        IOptions<AgentConfig> config,
        ILogger<ReviewOrchestrator> logger)
    {
        _kernelFactory = kernelFactory;
        _plugins = plugins;
        _knowledgeBase = knowledgeBase;
        _config = config.Value;
        _logger = logger;
    }

    /// <summary>专家角色定义：角色名 → 对应的 system prompt。</summary>
    private static readonly (string Role, Func<string> Prompt)[] Specialists =
    {
        ("风格", () => ReviewPrompts.Style),
        ("安全", () => ReviewPrompts.Security),
        ("性能", () => ReviewPrompts.Performance),
    };

    /// <summary>阶段1 + 阶段2：多专家并行评审 + 主审汇总。</summary>
    public async Task<ReviewResult> RunDeepReviewAsync(
        string targetDirectory, IAgentObserver? observer = null, CancellationToken ct = default)
    {
        observer ??= NullAgentObserver.Instance;
        var fullPath = Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"审查目录不存在：{fullPath}");
        }

        var sw = Stopwatch.StartNew();
        if (_config.Rag.Enabled)
        {
            await _knowledgeBase.InitializeAsync(ct); // 确保规范知识库已就绪
        }

        var goal = $"请审查目录「{fullPath}」中的 C# 代码。先列出文件，再针对关键文件读取并做静态分析，" +
                   $"最后从你的专长视角给出 Markdown 审查意见。";

        // 阶段1：三位专家并行（体现异步深度；各自独立 ReAct 循环与工作记忆）。
        var tasks = Specialists.Select(s => RunSpecialistAsync(s.Role, s.Prompt(), fullPath, goal, observer, ct));
        var specialistReviews = await Task.WhenAll(tasks);

        // 阶段2：主审汇总。
        var finalReport = await AggregateAsync(specialistReviews, observer, ct);

        sw.Stop();
        _logger.LogInformation("深度审查完成：{Dir}，用时 {Elapsed}，工具调用 {Tools} 次。",
            fullPath, sw.Elapsed, specialistReviews.Sum(s => s.ToolCalls));

        return new ReviewResult(fullPath, specialistReviews, finalReport, sw.Elapsed);
    }

    /// <summary>阶段3（可选）：修复 Agent 根据发现生成补丁并验证编译。</summary>
    public async Task<RemediationResult> RunRemediationAsync(
        string targetDirectory, string confirmedFindings, IAgentObserver? observer = null, CancellationToken ct = default)
    {
        observer ??= NullAgentObserver.Instance;
        var fullPath = Path.GetFullPath(targetDirectory);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"审查目录不存在：{fullPath}");
        }

        var sw = Stopwatch.StartNew();
        var kernel = _kernelFactory.CreateKernel(_plugins.BuildFixTools(fullPath));
        var memory = new ConversationMemory(ReviewPrompts.Fixer);
        var agent = new ReActAgent("修复", kernel, memory, _config.Agent, _config.Llm, observer);

        var goal = $"审查目录：{fullPath}\n以下是已确认的问题清单，请逐项尝试「修复→编译验证」：\n\n{confirmedFindings}";
        var result = await agent.RunAsync(goal, ct);
        sw.Stop();

        return new RemediationResult(fullPath, result.Answer, result.StepsUsed, result.ToolCallCount, sw.Elapsed);
    }

    /// <summary>运行单个评审专家（一个独立 ReAct 循环，挂载只读工具）。</summary>
    private async Task<SpecialistReview> RunSpecialistAsync(
        string role, string systemPrompt, string root, string goal, IAgentObserver observer, CancellationToken ct)
    {
        var kernel = _kernelFactory.CreateKernel(_plugins.BuildReviewTools(root));
        var memory = new ConversationMemory(systemPrompt);
        // 多专家并行时关闭逐 token 流式，避免多路 token 在同一界面交织；
        // 仍通过 observer 输出原子化的 步/动作/观察 事件。
        var options = new ReActOptions { MaxSteps = _config.Agent.MaxSteps, Streaming = false };
        var agent = new ReActAgent($"{role}专家", kernel, memory, options, _config.Llm, observer);

        var result = await agent.RunAsync(goal, ct);
        return new SpecialistReview(role, result.Answer, result.StepsUsed, result.ToolCallCount);
    }

    /// <summary>主审：把多份专家意见汇总为最终报告（无需工具，单步产出）。</summary>
    private async Task<string> AggregateAsync(
        IReadOnlyList<SpecialistReview> reviews, IAgentObserver observer, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var r in reviews)
        {
            sb.AppendLine($"### {r.Role}专家的审查意见").AppendLine(r.Markdown).AppendLine();
        }

        var kernel = _kernelFactory.CreateKernel(); // 主审不需要工具
        var memory = new ConversationMemory(ReviewPrompts.Aggregator);
        var options = new ReActOptions { MaxSteps = 2, Streaming = _config.Agent.Streaming };
        var aggregator = new ReActAgent("主审", kernel, memory, options, _config.Llm, observer);

        var result = await aggregator.RunAsync(sb.ToString(), ct);
        return result.Answer;
    }
}
