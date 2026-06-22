using System.ComponentModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Tools;

/// <summary>
/// 编译验证工具（组员 B 负责）：本项目「读 → 改 → 验」闭环里的「验」。
/// 把（修改后的）C# 源码用 Roslyn 编译到内存程序集，返回编译诊断，
/// 让修复 Agent 能确认「改完没把代码改坏」。注意：这是确定性工具，无需 LLM 判断。
/// </summary>
public sealed class CompileCheckPlugin
{
    private readonly SandboxedPathResolver _paths;

    public CompileCheckPlugin(string root)
    {
        _paths = new SandboxedPathResolver(root);
    }

    [KernelFunction("compile_check")]
    [Description("用 Roslyn 把指定 C# 文件编译为内存程序集，返回编译诊断（错误/警告），验证代码能否通过编译。")]
    public string CompileCheck(
        [Description("相对审查根目录的 .cs 文件路径")] string path)
    {
        var fullPath = _paths.ResolveExistingCSharpFile(path);
        var source = File.ReadAllText(fullPath);
        var tree = CSharpSyntaxTree.ParseText(source, path: _paths.ToRelativeDisplayPath(fullPath));
        var compilation = CSharpCompilation.Create(
            $"CodeReviewCheck_{Guid.NewGuid():N}",
            new[] { tree },
            GetTrustedPlatformReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var assembly = new MemoryStream();
        var result = compilation.Emit(assembly);
        var diagnostics = result.Diagnostics
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .OrderBy(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Line)
            .ThenBy(diagnostic => diagnostic.Location.GetLineSpan().StartLinePosition.Character)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .Select(FormatDiagnostic)
            .ToArray();

        var status = result.Success ? "Success" : "Failed";
        return diagnostics.Length == 0
            ? status
            : $"{status}{Environment.NewLine}{string.Join(Environment.NewLine, diagnostics)}";
    }

    private static IReadOnlyList<MetadataReference> GetTrustedPlatformReferences()
    {
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            throw new InvalidOperationException("当前运行时未提供可信平台程序集列表。 ");
        }

        return trustedAssemblies.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToArray();
    }

    private static string FormatDiagnostic(Diagnostic diagnostic)
    {
        var start = diagnostic.Location.GetLineSpan().StartLinePosition;
        return $"{diagnostic.Severity} {diagnostic.Id} | {start.Line + 1}:{start.Character + 1} | {diagnostic.GetMessage()}";
    }
}
