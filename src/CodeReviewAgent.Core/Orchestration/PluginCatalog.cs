using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Rag;
using CodeReviewAgent.Core.Tools;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Orchestration;

/// <summary>
/// 工具「组装缝」：集中负责把各工具类实例化、按场景组合成 Kernel 插件集合。
///
/// 这是 A（引擎）与 B（工具）之间唯一的紧耦合点 —— 本类直接 new 出 B 实现的工具类。
/// 并行开发期，B 的工具可以是桩实现；只要类名/构造签名符合「工具契约」，本类即可编译，
/// 引擎一侧无需等待 B 完成即可推进。
///
/// 工具按场景分组（不同角色挂不同工具）：
/// - 评审工具（只读）：list_files / read_file / analyze_code / search_standards。
/// - 修复工具（读 + 写 + 验证）：在评审工具基础上加 propose_fix / compile_check。
/// - 对话工具：以上全部（顶层对话 Agent 拥有完整工具箱）。
/// </summary>
public sealed class PluginCatalog
{
    private readonly IKnowledgeBase _knowledgeBase;
    private readonly int _ragTopK;

    public PluginCatalog(IKnowledgeBase knowledgeBase, IOptions<AgentConfig> config)
    {
        _knowledgeBase = knowledgeBase;
        _ragTopK = config.Value.Rag.TopK;
    }

    /// <summary>评审专家用的只读工具集（沙箱绑定到审查根目录）。</summary>
    public IReadOnlyList<KernelPlugin> BuildReviewTools(string root) => new List<KernelPlugin>
    {
        KernelPluginFactory.CreateFromObject(new FileSystemPlugin(root), "files"),
        KernelPluginFactory.CreateFromObject(new RoslynAnalysisPlugin(root), "roslyn"),
        KernelPluginFactory.CreateFromObject(new CodingStandardsPlugin(_knowledgeBase, _ragTopK), "standards"),
    };

    /// <summary>修复 Agent 用的工具集：读 + 改 + 验证。</summary>
    public IReadOnlyList<KernelPlugin> BuildFixTools(string root) => new List<KernelPlugin>
    {
        KernelPluginFactory.CreateFromObject(new FileSystemPlugin(root), "files"),
        KernelPluginFactory.CreateFromObject(new FixPlugin(root), "fix"),
        KernelPluginFactory.CreateFromObject(new CompileCheckPlugin(root), "compile"),
    };

    /// <summary>
    /// 顶层对话 Agent 的细粒度工具箱（不含粗粒度动作，后者单独挂载）。
    /// 对外部目录默认只加载只读工具；只有调用方得到明确写入授权时才加入修复工具。
    /// </summary>
    public IReadOnlyList<KernelPlugin> BuildConversationTools(
        string root,
        bool includeWriteTools = true,
        FixSession? fixSession = null)
    {
        var tools = new List<KernelPlugin>
        {
            KernelPluginFactory.CreateFromObject(new FileSystemPlugin(root), "files"),
            KernelPluginFactory.CreateFromObject(new RoslynAnalysisPlugin(root), "roslyn"),
            KernelPluginFactory.CreateFromObject(new CodingStandardsPlugin(_knowledgeBase, _ragTopK), "standards"),
        };

        if (includeWriteTools)
        {
            if (fixSession is not null && !string.Equals(
                    Path.GetFullPath(root), Path.GetFullPath(fixSession.Root), StringComparison.Ordinal))
            {
                throw new ArgumentException("修复会话必须绑定到同一个审查根目录。", nameof(fixSession));
            }
            tools.Add(KernelPluginFactory.CreateFromObject(
                fixSession is null ? new FixPlugin(root) : new FixPlugin(fixSession), "fix"));
            tools.Add(KernelPluginFactory.CreateFromObject(new CompileCheckPlugin(root), "compile"));
        }

        return tools;
    }
}
