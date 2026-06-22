namespace CodeReviewAgent.Core.Tools;

/// <summary>Resolves untrusted relative paths while keeping access inside one review root.</summary>
internal sealed class SandboxedPathResolver
{
    private readonly string _root;
    private readonly string _rootPrefix;
    private readonly StringComparison _comparison;

    public SandboxedPathResolver(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("审查根目录不能为空。", nameof(root));
        }

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        if (!Directory.Exists(_root))
        {
            throw new DirectoryNotFoundException($"审查根目录不存在：{_root}");
        }

        _rootPrefix = _root + Path.DirectorySeparatorChar;
        _comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
    }

    public string Root => _root;

    public string ResolveExistingCSharpFile(string relativePath)
    {
        var fullPath = ResolveRelative(relativePath);
        if (!string.Equals(Path.GetExtension(fullPath), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("只允许访问 .cs 源文件。", nameof(relativePath));
        }
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("源文件不存在。", relativePath);
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    public string ResolveExistingDirectory(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return _root;
        }

        var fullPath = ResolveRelative(relativePath);
        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryNotFoundException($"子目录不存在：{relativePath}");
        }

        EnsureNoReparsePoints(fullPath);
        return fullPath;
    }

    public string ToRelativeDisplayPath(string fullPath) =>
        Path.GetRelativePath(_root, fullPath).Replace(Path.DirectorySeparatorChar, '/');

    private string ResolveRelative(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("路径不能为空。", nameof(relativePath));
        }
        if (Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException("只允许使用相对审查根目录的路径。", nameof(relativePath));
        }

        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        if (!fullPath.StartsWith(_rootPrefix, _comparison))
        {
            throw new UnauthorizedAccessException("路径越出了审查根目录。 ");
        }

        return fullPath;
    }

    private void EnsureNoReparsePoints(string fullPath)
    {
        var current = fullPath;
        while (!string.Equals(current, _root, _comparison))
        {
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("不允许通过符号链接或重解析点访问文件。 ");
            }

            current = Path.GetDirectoryName(current)
                ?? throw new UnauthorizedAccessException("无法验证路径边界。 ");
        }
    }
}
