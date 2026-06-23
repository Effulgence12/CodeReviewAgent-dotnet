using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Core.Configuration;

/// <summary>
/// 为 Web 与 MCP 统一解析不可信的审查目标路径。
/// 该策略只决定「能否选中一个目录/文件」；目录内工具仍会使用各自的沙箱解析器，
/// 因而工具参数始终不能跳出最终选定的审查根目录。
/// </summary>
public sealed class ReviewDirectoryPolicy
{
    private readonly ReviewAccessOptions _options;

    public ReviewDirectoryPolicy(IOptions<AgentConfig> config)
    {
        _options = config.Value.ReviewAccess;
    }

    /// <summary>解析并验证一个审查目录，返回规范化绝对路径。</summary>
    public string ResolveDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("审查目录不能为空。", nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim());
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"审查目录不存在：{fullPath}");
        }

        EnsureAllowed(fullPath);
        return fullPath;
    }

    /// <summary>解析并验证一个供 MCP 静态分析使用的 C# 文件。</summary>
    public string ResolveCSharpFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("C# 文件路径不能为空。", nameof(path));
        }

        var fullPath = Path.GetFullPath(path.Trim());
        if (!string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("只允许分析 .cs 文件。", nameof(path));
        }
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("C# 文件不存在。", fullPath);
        }

        EnsureAllowed(fullPath);
        return fullPath;
    }

    private void EnsureAllowed(string fullPath)
    {
        if (_options.AllowArbitraryDirectories)
        {
            return;
        }

        var roots = _options.SharedRoots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)))
            .ToArray();

        if (roots.Any(root => IsWithin(fullPath, root)))
        {
            return;
        }

        var hint = roots.Length == 0
            ? "当前未配置任何共享目录。"
            : $"允许的共享目录：{string.Join("；", roots)}";
        throw new UnauthorizedAccessException($"目标路径不在允许的审查目录中。{hint}");
    }

    private static bool IsWithin(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return string.Equals(normalizedPath, normalizedRoot, comparison) ||
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }
}
