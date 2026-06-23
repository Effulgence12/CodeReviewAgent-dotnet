using CodeReviewAgent.Core.Abstractions;
using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Llm;
using CodeReviewAgent.Core.Memory;
using CodeReviewAgent.Core.Orchestration;
using CodeReviewAgent.Core.Tools;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Agent;

/// <summary>
/// 顶层「对话式编排 Agent」的工厂 —— 系统唯一入口的装配点。
///
/// 它创建一个 <see cref="ReActAgent"/>，挂载完整工具箱：
///   细粒度工具（list_files / read_file / analyze_code / search_standards / propose_fix / compile_check）
///   + 粗粒度动作（run_deep_review / run_remediation，来自 <see cref="ReviewActionsPlugin"/>）。
/// 由 LLM 在多轮对话中**动态决定**每一步调用哪个 —— 这就是「不区分单/多 Agent 模式」：
/// 多 Agent 只是它的一个动作。
///
/// 用法（前端，如 Web）：
///   每个聊天会话 Create 一次，得到一个长生命周期的对话 Agent；
///   之后每轮用户输入调用 <see cref="ReActAgent.RunAsync"/>，记忆在多轮间累积。
/// </summary>
public sealed class ConversationalAgentFactory
{
    private readonly KernelFactory _kernelFactory;
    private readonly PluginCatalog _plugins;
    private readonly ReviewOrchestrator _orchestrator;
    private readonly AgentConfig _config;

    public ConversationalAgentFactory(
        KernelFactory kernelFactory,
        PluginCatalog plugins,
        ReviewOrchestrator orchestrator,
        IOptions<AgentConfig> config)
    {
        _kernelFactory = kernelFactory;
        _plugins = plugins;
        _orchestrator = orchestrator;
        _config = config.Value;
    }

    /// <summary>
    /// 为一个聊天会话创建对话 Agent。
    /// </summary>
    /// <param name="reviewRoot">本会话的审查根目录（工具与深度审查都沙箱绑定到它）。</param>
    /// <param name="observer">推理过程观察者（Web 用于实时渲染轨迹）。</param>
    public ReActAgent Create(
        string reviewRoot,
        IAgentObserver? observer = null,
        bool allowWrites = true,
        FixSession? fixSession = null)
    {
        var root = Path.GetFullPath(reviewRoot);

        // 完整工具箱 = 细粒度工具 + 粗粒度动作。
        var tools = new List<KernelPlugin>(_plugins.BuildConversationTools(root, allowWrites, fixSession));
        // 交互 Web 会话使用暂存补丁，不能暴露会直接写文件的整包 remediation 动作。
        object reviewActions = allowWrites && fixSession is null
            ? new ReviewActionsPlugin(_orchestrator, root, observer)
            : new ReadOnlyReviewActionsPlugin(_orchestrator, root, observer);
        tools.Add(KernelPluginFactory.CreateFromObject(reviewActions, "review"));

        var kernel = _kernelFactory.CreateKernel(tools);
        var memory = new ConversationMemory(ReviewPrompts.Conversational);
        return new ReActAgent("对话助手", kernel, memory, _config.Agent, _config.Llm, observer);
    }
}
