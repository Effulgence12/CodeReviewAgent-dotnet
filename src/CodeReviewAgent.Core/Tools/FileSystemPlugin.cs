using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Tools;

/// <summary>
/// 文件系统工具（组员 B 负责）：让 Agent 能浏览与读取待审查代码。
/// 沙箱：所有路径都相对构造时传入的 <c>root</c>，实现时需防目录穿越（禁止访问 root 之外）。
/// </summary>
public sealed class FileSystemPlugin
{
    private readonly SandboxedPathResolver _paths;

    /// <param name="root">审查根目录；本插件所有访问都限定在此目录内。</param>
    public FileSystemPlugin(string root)
    {
        _paths = new SandboxedPathResolver(root);
    }

    [KernelFunction("list_files")]
    [Description("列出审查目录下的源代码文件（递归）。返回相对路径列表，可用 pattern 过滤，如 *.cs。")]
    public string ListFiles(
        [Description("相对审查根目录的子目录，留空表示根目录")] string subDirectory = "",
        [Description("文件名通配符，如 *.cs；留空表示 *.cs")] string pattern = "*.cs")
    {
        pattern = string.IsNullOrWhiteSpace(pattern) ? "*.cs" : pattern.Trim();
        if (Path.GetFileName(pattern) != pattern ||
            pattern.Contains("..", StringComparison.Ordinal) ||
            pattern.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, ':' }) >= 0)
        {
            throw new ArgumentException("pattern 只能是简单文件名通配符，不能包含目录。", nameof(pattern));
        }

        var directory = _paths.ResolveExistingDirectory(subDirectory);
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        var files = Directory.EnumerateFiles(directory, pattern, options)
            .Where(path => string.Equals(Path.GetExtension(path), ".cs", StringComparison.OrdinalIgnoreCase))
            .Select(_paths.ToRelativeDisplayPath)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return files.Length == 0
            ? "未找到匹配的 C# 源文件。"
            : string.Join(Environment.NewLine, files);
    }

    [KernelFunction("read_file")]
    [Description("读取指定源代码文件的内容，带行号，便于在审查中精确定位到行。")]
    public string ReadFile(
        [Description("相对审查根目录的文件路径，如 Services/Order.cs")] string path)
    {
        var fullPath = _paths.ResolveExistingCSharpFile(path);
        var content = File.ReadAllText(fullPath);
        if (content.IndexOf('\0') >= 0)
        {
            throw new InvalidDataException("源文件包含二进制内容，无法作为文本读取。 ");
        }

        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        return string.Join(Environment.NewLine, lines.Select((line, index) => $"{index + 1}: {line}"));
    }
}
