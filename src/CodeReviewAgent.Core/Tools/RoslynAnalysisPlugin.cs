using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Tools;

/// <summary>
/// Roslyn 静态分析工具（组员 B 负责）：对单个 C# 文件做客观的语法/规则级分析。
/// 这是「真实工具调用」的核心证据来源 —— 结论基于编译器 API，而非模型臆测。
/// </summary>
public sealed class RoslynAnalysisPlugin
{
    private readonly string _root;

    public RoslynAnalysisPlugin(string root)
    {
        _root = Path.GetFullPath(root);
    }

    [KernelFunction("analyze_code")]
    [Description("用 Roslyn 对单个 C# 文件做静态分析，返回客观发现（语法诊断、空 catch、超长方法、魔法数、async 缺 await、命名问题、TODO 等），含行号。")]
    public string AnalyzeCode(
        [Description("相对审查根目录的 .cs 文件路径")] string path)
    {
        // TODO(组员B): 读取文件 → 调 Analyze(code) → 格式化为带行号的发现列表返回。
        throw new NotImplementedException("RoslynAnalysisPlugin.AnalyzeCode 待组员 B 实现。");
    }

    /// <summary>
    /// 纯分析核心（无 IO，便于单测与被 MCP 等复用）：输入源码字符串，返回可读的发现文本。
    /// </summary>
    public static string Analyze(string code)
    {
        // TODO(组员B): 用 CSharpSyntaxTree.ParseText(code) 遍历语法树，产出诊断列表（含行号）。
        throw new NotImplementedException("RoslynAnalysisPlugin.Analyze 待组员 B 实现。");
    }
}
