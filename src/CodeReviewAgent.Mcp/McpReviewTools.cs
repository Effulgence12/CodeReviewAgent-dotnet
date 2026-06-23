using System.ComponentModel;
using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Orchestration;
using CodeReviewAgent.Core.Tools;
using Microsoft.Extensions.Logging;
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
        ReviewDirectoryPolicy directoryPolicy,
        [Description("要分析的 .cs 文件路径")] string path)
    {
        try
        {
            var fullPath = directoryPolicy.ResolveCSharpFile(path);
            var code = File.ReadAllText(fullPath);
            // 复用 Core 的纯分析核心（不需要 LLM）。
            return RoslynAnalysisPlugin.Analyze(code);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException or UnauthorizedAccessException)
        {
            return $"无法分析文件：{ex.Message}";
        }
    }

    [McpServerTool(Name = "review_directory")]
    [Description("对一个目录运行完整的多 Agent 代码审查（风格/安全/性能专家并行 + 主审汇总），返回 Markdown 审查报告。需要已配置 LLM Key。")]
    public static async Task<string> ReviewDirectory(
        ReviewOrchestrator orchestrator,
        ReviewDirectoryPolicy directoryPolicy,
        ILogger<McpReviewTools> logger,
        [Description("要审查的目录路径")] string path,
        CancellationToken cancellationToken)
    {
        try
        {
            var fullPath = directoryPolicy.ResolveDirectory(path);
            // MCP 当前只暴露只读的深度审查；写入始终需要在 Web 中显式授权。
            var result = await orchestrator.RunDeepReviewAsync(fullPath, observer: null, cancellationToken);
            return $"# 审查报告：{result.TargetDirectory}\n\n用时 {result.Elapsed.TotalSeconds:F1}s，" +
                   $"工具调用 {result.TotalToolCalls} 次。\n\n{result.FinalReport}";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            logger.LogInformation("MCP 深度审查已取消：{Path}", path);
            return "深度审查已取消。";
        }
        catch (Exception ex)
        {
            // Inspector 只会把未捕获异常显示为通用的 “An error occurred invoking …”。
            // 保留完整异常到 stderr，同时把可行动的诊断文本作为正常 MCP 工具结果返回。
            logger.LogError(ex, "MCP 深度审查失败：{Path}", path);
            return DescribeReviewFailure(ex);
        }
    }

    private static string DescribeReviewFailure(Exception exception) => exception switch
    {
        ArgumentException or DirectoryNotFoundException or UnauthorizedAccessException =>
            $"无法审查目录：{exception.Message}",
        HttpRequestException =>
            "深度审查失败：模型或 Embedding API 请求失败。请检查 Llm/Embedding 的 Endpoint、ApiKey、ChatModel/Model，以及网络连通性；完整响应已写入 MCP 服务的 stderr 日志。",
        InvalidOperationException =>
            $"深度审查失败：运行配置或知识库不可用。{exception.Message}",
        _ =>
            "深度审查失败：服务端发生未预期错误。请查看 MCP 服务 stderr 中的完整异常信息。",
    };
}
