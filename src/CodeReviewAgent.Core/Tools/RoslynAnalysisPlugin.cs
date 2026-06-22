using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Tools;

/// <summary>
/// Roslyn 静态分析工具（组员 B 负责）：对单个 C# 文件做客观的语法/规则级分析。
/// 这是「真实工具调用」的核心证据来源 —— 结论基于编译器 API，而非模型臆测。
/// </summary>
public sealed class RoslynAnalysisPlugin
{
    private const int LongMethodLineThreshold = 50;
    private static readonly Regex MaintenanceMarker = new(
        @"\b(TODO|FIXME|HACK)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly SandboxedPathResolver _paths;

    public RoslynAnalysisPlugin(string root)
    {
        _paths = new SandboxedPathResolver(root);
    }

    [KernelFunction("analyze_code")]
    [Description("用 Roslyn 对单个 C# 文件做静态分析，返回客观发现（语法诊断、空 catch、超长方法、魔法数、async 缺 await、命名问题、TODO 等），含行号。")]
    public string AnalyzeCode(
        [Description("相对审查根目录的 .cs 文件路径")] string path)
    {
        var fullPath = _paths.ResolveExistingCSharpFile(path);
        var relativePath = _paths.ToRelativeDisplayPath(fullPath);
        var code = File.ReadAllText(fullPath);
        if (code.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("源文件包含二进制内容，无法分析。 ");
        }

        return $"文件: {relativePath}{Environment.NewLine}{Analyze(code)}";
    }

    /// <summary>
    /// 纯分析核心（无 IO，便于单测与被 MCP 等复用）：输入源码字符串，返回可读的发现文本。
    /// </summary>
    public static string Analyze(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var tree = CSharpSyntaxTree.ParseText(code);
        var findings = tree.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning)
            .Select(diagnostic => Finding.FromLocation(
                "CRA000",
                diagnostic.Severity == DiagnosticSeverity.Error ? "Error" : "Warning",
                diagnostic.Location,
                $"C# {diagnostic.Id}: {diagnostic.GetMessage()}"))
            .ToList();

        findings.AddRange(tree.GetRoot().DescendantNodes()
            .OfType<CatchClauseSyntax>()
            .Where(catchClause => catchClause.Block.Statements.Count == 0)
            .Select(catchClause => Finding.FromLocation(
                "CRA001", "Warning", catchClause.CatchKeyword.GetLocation(), "空 catch 会吞掉异常。")));

        var methodLikeNodes = tree.GetRoot().DescendantNodes()
            .Where(node => node is MethodDeclarationSyntax or ConstructorDeclarationSyntax or LocalFunctionStatementSyntax)
            .Where(node => GetPhysicalLineCount(node) > LongMethodLineThreshold);
        findings.AddRange(methodLikeNodes.Select(node => Finding.FromLocation(
            "CRA002",
            "Warning",
            node.GetLocation(),
            $"方法跨度超过 {LongMethodLineThreshold} 行，建议拆分。")));

        findings.AddRange(tree.GetRoot().DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .Where(literal => literal.IsKind(SyntaxKind.NumericLiteralExpression))
            .Where(literal => !IsAllowedNumericLiteral(literal))
            .Select(literal => Finding.FromLocation(
                "CRA003", "Suggestion", literal.GetLocation(), $"数字 {literal.Token.Text} 缺少具名含义，考虑提取常量。")));

        findings.AddRange(tree.GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .Where(method => method.Modifiers.Any(SyntaxKind.AsyncKeyword))
            .Where(method => !HasDirectAwait(method))
            .Select(method => Finding.FromLocation(
                "CRA004", "Warning", method.Identifier.GetLocation(), "async 方法中没有直属 await。")));

        var root = tree.GetRoot();
        findings.AddRange(root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
            .Where(type => !IsPascalCase(type.Identifier.ValueText))
            .Select(type => NamingFinding(type.Identifier, "类型名应使用 PascalCase。")));
        findings.AddRange(root.DescendantNodes().OfType<DelegateDeclarationSyntax>()
            .Where(type => !IsPascalCase(type.Identifier.ValueText))
            .Select(type => NamingFinding(type.Identifier, "委托名应使用 PascalCase。")));
        findings.AddRange(root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => !IsPascalCase(method.Identifier.ValueText))
            .Select(method => NamingFinding(method.Identifier, "方法名应使用 PascalCase。")));
        findings.AddRange(root.DescendantNodes().OfType<PropertyDeclarationSyntax>()
            .Where(property => !IsPascalCase(property.Identifier.ValueText))
            .Select(property => NamingFinding(property.Identifier, "属性名应使用 PascalCase。")));
        findings.AddRange(root.DescendantNodes().OfType<ParameterSyntax>()
            .Where(parameter => parameter.Identifier.ValueText != "_" && !IsCamelCase(parameter.Identifier.ValueText))
            .Select(parameter => NamingFinding(parameter.Identifier, "参数名应使用 camelCase。")));
        findings.AddRange(root.DescendantNodes().OfType<VariableDeclaratorSyntax>()
            .Where(variable => variable.Parent?.Parent is LocalDeclarationStatementSyntax)
            .Where(variable => variable.Identifier.ValueText != "_" && !IsCamelCase(variable.Identifier.ValueText))
            .Select(variable => NamingFinding(variable.Identifier, "局部变量名应使用 camelCase。")));

        var commentKinds = new[]
        {
            SyntaxKind.SingleLineCommentTrivia,
            SyntaxKind.MultiLineCommentTrivia,
            SyntaxKind.SingleLineDocumentationCommentTrivia,
            SyntaxKind.MultiLineDocumentationCommentTrivia,
        };
        foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true)
                     .Where(item => commentKinds.Contains(item.Kind())))
        {
            foreach (Match match in MaintenanceMarker.Matches(trivia.ToFullString()))
            {
                var location = Location.Create(tree, new TextSpan(trivia.FullSpan.Start + match.Index, match.Length));
                findings.Add(Finding.FromLocation(
                    "CRA006", "Suggestion", location, $"发现维护标记 {match.Value.ToUpperInvariant()}。"));
            }
        }

        var ordered = findings.OrderBy(finding => finding.Line)
            .ThenBy(finding => finding.Column)
            .ThenBy(finding => finding.RuleId, StringComparer.Ordinal)
            .ToArray();

        return Format(ordered);
    }

    private static string Format(IReadOnlyList<Finding> findings) =>
        findings.Count == 0
            ? "未发现静态分析问题。"
            : string.Join(Environment.NewLine, findings.Select(finding => finding.ToString()));

    private static int GetPhysicalLineCount(SyntaxNode node)
    {
        var span = node.GetLocation().GetLineSpan();
        return span.EndLinePosition.Line - span.StartLinePosition.Line + 1;
    }

    private static bool IsAllowedNumericLiteral(LiteralExpressionSyntax literal)
    {
        if (literal.Ancestors().OfType<EnumMemberDeclarationSyntax>().Any())
        {
            return true;
        }

        var variable = literal.Ancestors().OfType<VariableDeclarationSyntax>().FirstOrDefault();
        if (variable?.Parent is FieldDeclarationSyntax field &&
            field.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return true;
        }
        if (variable?.Parent is LocalDeclarationStatementSyntax local &&
            local.Modifiers.Any(SyntaxKind.ConstKeyword))
        {
            return true;
        }

        if (!double.TryParse(literal.Token.ValueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }
        if (literal.Parent is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            value = -value;
        }

        return value is -1 or 0 or 1 or 2;
    }

    private static bool HasDirectAwait(MethodDeclarationSyntax method) =>
        method.DescendantNodes()
            .OfType<AwaitExpressionSyntax>()
            .Any(awaitExpression => !awaitExpression.Ancestors()
                .TakeWhile(ancestor => ancestor != method)
                .Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax));

    private static bool IsPascalCase(string name) =>
        name.Length > 0 && char.IsUpper(name[0]);

    private static bool IsCamelCase(string name) =>
        name.Length > 0 && char.IsLower(name[0]);

    private static Finding NamingFinding(SyntaxToken identifier, string message) =>
        Finding.FromLocation("CRA005", "Suggestion", identifier.GetLocation(), message);

    private sealed record Finding(string RuleId, string Severity, int Line, int Column, string Message)
    {
        public static Finding FromLocation(
            string ruleId, string severity, Location location, string message)
        {
            var start = location.IsInSource
                ? location.GetLineSpan().StartLinePosition
                : new LinePosition(0, 0);
            return new Finding(ruleId, severity, start.Line + 1, start.Character + 1, message);
        }

        public override string ToString() =>
            $"{RuleId} | {Severity} | {Line}:{Column} | {Message}";
    }
}
