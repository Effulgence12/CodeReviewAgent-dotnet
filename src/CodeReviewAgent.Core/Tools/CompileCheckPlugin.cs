using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Tools;

/// <summary>
/// 编译验证工具（组员 B 负责）：本项目「读 → 改 → 验」闭环里的「验」。
/// 把（修改后的）C# 源码用 Roslyn 编译到内存程序集，返回编译诊断，
/// 让修复 Agent 能确认「改完没把代码改坏」。注意：这是确定性工具，无需 LLM 判断。
/// </summary>
public sealed class CompileCheckPlugin
{
    private readonly string _root;

    public CompileCheckPlugin(string root)
    {
        _root = Path.GetFullPath(root);
    }

    [KernelFunction("compile_check")]
    [Description("用 Roslyn 把指定 C# 文件编译为内存程序集，返回编译诊断（错误/警告），验证代码能否通过编译。")]
    public string CompileCheck(
        [Description("相对审查根目录的 .cs 文件路径")] string path)
    {
        // TODO(组员B): 读取文件 → CSharpCompilation.Create(...).Emit(内存流) → 汇总 GetDiagnostics() 返回。
        //             需要引用基础程序集（如 System.Private.CoreLib、System.Runtime）作为 MetadataReference。
        throw new NotImplementedException("CompileCheckPlugin.CompileCheck 待组员 B 实现。");
    }
}
