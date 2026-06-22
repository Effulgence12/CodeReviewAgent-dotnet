using System.ComponentModel;
using CodeReviewAgent.Core.Orchestration;
using CodeReviewAgent.Core.Tools;
using ModelContextProtocol.Server;

namespace CodeReviewAgent.Mcp;

/// <summary>
/// 把代码审查能力暴露为 MCP（Model Context Protocol）工具，供任意 MCP 客户端
/// （Claude Desktop、MCP Inspector、其它 Agent，甚至手工 JSON-RPC）调用。
///
/// 注意「MCP 工具」与「Agent 内部工具」的区别：
/// - Core/Tools 里的 [KernelFunction] 是给本项目 Agent 内部循环用的「手」（进程内）。
/// - 这里的 [McpServerTool] 是把整套能力打包成标准协议接口，供外部程序跨进程调用。
/// 二者都复用 Core 的同一套实现，无重复业务逻辑。
///
/// 组员 C 负责本文件；具体分析/审查逻辑依赖组员 B（工具）与组员 A（编排器）落地后生效。
/// </summary>
[McpServerToolType]
public sealed class McpReviewTools
{
    [McpServerTool(Name = "analyze_csharp")]
    [Description("用 Roslyn 对一个 C# 文件做静态分析，返回客观发现（空 catch、超长方法、魔法数、async 缺 await、命名、TODO 等），含行号。不需要 LLM。")]
    public static string AnalyzeCSharp(
        [Description("要分析的 .cs 文件路径")] string path)
    {
        if (!File.Exists(path))
        {
            return $"文件不存在：{path}";
        }
        var code = File.ReadAllText(path);
        // 复用 Core 的纯分析核心（组员 B 落地后生效）。
        return RoslynAnalysisPlugin.Analyze(code);
    }

    [McpServerTool(Name = "review_directory")]
    [Description("对一个目录运行完整的多 Agent 代码审查（风格/安全/性能专家并行 + 主审汇总），返回 Markdown 审查报告。需要已配置 LLM Key。")]
    public static async Task<string> ReviewDirectory(
        ReviewOrchestrator orchestrator,
        [Description("要审查的目录路径")] string path,
        CancellationToken cancellationToken)
    {
        // 复用 Core 的多 Agent 编排器（agents-as-tools：外层 MCP 工具触发内层多 Agent 工作流）。
        var result = await orchestrator.RunDeepReviewAsync(path, observer: null, cancellationToken);
        return $"# 审查报告：{result.TargetDirectory}\n\n用时 {result.Elapsed.TotalSeconds:F1}s，" +
               $"工具调用 {result.TotalToolCalls} 次。\n\n{result.FinalReport}";
    }
}
