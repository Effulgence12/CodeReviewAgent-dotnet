using System.Text;

namespace CodeReviewAgent.Core.Tools;

/// <summary>
/// 一个交互会话内的待确认修复集合。模型只能通过 <see cref="Stage"/> 生成补丁；
/// 由宿主 UI 在用户明确确认后调用 <see cref="Apply"/> 写入真实文件。
/// </summary>
public sealed class FixSession
{
    private readonly SandboxedPathResolver _paths;
    private readonly Dictionary<string, PendingFix> _pending = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    public FixSession(string root)
    {
        _paths = new SandboxedPathResolver(root);
    }

    public string Root => _paths.Root;

    public IReadOnlyList<PendingFix> PendingFixes
    {
        get
        {
            lock (_sync)
            {
                return _pending.Values
                    .OrderBy(fix => fix.CreatedAt)
                    .ToArray();
            }
        }
    }

    /// <summary>读取原文件并生成待确认补丁，不写入任何源文件。</summary>
    public PendingFix Stage(string path, string issue, string newContent)
    {
        if (string.IsNullOrWhiteSpace(issue))
        {
            throw new ArgumentException("问题描述不能为空。", nameof(issue));
        }
        if (string.IsNullOrWhiteSpace(newContent))
        {
            throw new ArgumentException("修改后的文件内容不能为空。", nameof(newContent));
        }

        var sourcePath = _paths.ResolveExistingCSharpFile(path);
        var originalContent = File.ReadAllText(sourcePath);
        var relativePath = _paths.ToRelativeDisplayPath(sourcePath);
        var fix = new PendingFix(
            Id: Guid.NewGuid().ToString("N"),
            Path: relativePath,
            Issue: issue.Trim(),
            OriginalContent: originalContent,
            NewContent: newContent,
            UnifiedDiff: CreateUnifiedDiff(relativePath, originalContent, newContent),
            CreatedAt: DateTimeOffset.Now);

        lock (_sync)
        {
            _pending.Add(fix.Id, fix);
        }
        return fix;
    }

    public void Discard(string fixId)
    {
        lock (_sync)
        {
            if (!_pending.Remove(fixId))
            {
                throw new KeyNotFoundException("待确认的修复不存在或已被处理。 ");
            }
        }
    }

    /// <summary>确认一个补丁；原文件被首次修改前会创建 .bak 备份。</summary>
    public AppliedFix Apply(string fixId)
    {
        PendingFix fix;
        lock (_sync)
        {
            if (!_pending.TryGetValue(fixId, out fix!))
            {
                throw new KeyNotFoundException("待确认的修复不存在或已被处理。 ");
            }
        }

        var sourcePath = _paths.ResolveExistingCSharpFile(fix.Path);
        var currentContent = File.ReadAllText(sourcePath);
        if (!string.Equals(currentContent, fix.OriginalContent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("源文件在生成补丁后已变化。请重新审查并生成新的修复建议。 ");
        }

        var backupPath = EnsureBackup(sourcePath);
        WriteAtomically(sourcePath, fix.NewContent);

        lock (_sync)
        {
            _pending.Remove(fixId);
        }

        return new AppliedFix(
            fix.Id,
            fix.Path,
            _paths.ToRelativeDisplayPath(backupPath),
            fix.Issue,
            DateTimeOffset.Now);
    }

    /// <summary>读取指定源文件的 .bak 内容，供 UI 预览。</summary>
    public string ReadBackup(string path)
    {
        var sourcePath = _paths.ResolveExistingCSharpFile(path);
        var backupPath = sourcePath + ".bak";
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("尚未找到该文件的 .bak 备份。", _paths.ToRelativeDisplayPath(backupPath));
        }
        if ((File.GetAttributes(backupPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException("备份文件不能是符号链接或重解析点。 ");
        }
        return File.ReadAllText(backupPath);
    }

    /// <summary>将 .bak 备份原子化恢复到源文件；保留备份以便再次查看。</summary>
    public RestoredFix Rollback(string path)
    {
        var sourcePath = _paths.ResolveExistingCSharpFile(path);
        var backupPath = sourcePath + ".bak";
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException("尚未找到可回滚的 .bak 备份。", _paths.ToRelativeDisplayPath(backupPath));
        }
        if ((File.GetAttributes(backupPath) & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException("备份文件不能是符号链接或重解析点。 ");
        }

        WriteAtomically(sourcePath, File.ReadAllText(backupPath));
        return new RestoredFix(_paths.ToRelativeDisplayPath(sourcePath), _paths.ToRelativeDisplayPath(backupPath), DateTimeOffset.Now);
    }

    private string EnsureBackup(string sourcePath)
    {
        var backupPath = sourcePath + ".bak";
        if (File.Exists(backupPath))
        {
            if ((File.GetAttributes(backupPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new UnauthorizedAccessException("备份文件不能是符号链接或重解析点。 ");
            }
            return backupPath;
        }

        File.Copy(sourcePath, backupPath, overwrite: false);
        return backupPath;
    }

    private static void WriteAtomically(string destinationPath, string content)
    {
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(destinationPath)!,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporaryPath, content, new UTF8Encoding(false));
            File.Move(temporaryPath, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string CreateUnifiedDiff(string path, string original, string updated)
    {
        var originalLines = NormalizeLines(original);
        var updatedLines = NormalizeLines(updated);
        var builder = new StringBuilder();
        builder.AppendLine($"--- a/{path}");
        builder.AppendLine($"+++ b/{path}");
        builder.AppendLine($"@@ -1,{originalLines.Length} +1,{updatedLines.Length} @@");
        foreach (var line in originalLines)
        {
            builder.Append('-').AppendLine(line);
        }
        foreach (var line in updatedLines)
        {
            builder.Append('+').AppendLine(line);
        }
        return builder.ToString();
    }

    private static string[] NormalizeLines(string content) => content
        .Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace('\r', '\n')
        .Split('\n');
}

public sealed record PendingFix(
    string Id,
    string Path,
    string Issue,
    string OriginalContent,
    string NewContent,
    string UnifiedDiff,
    DateTimeOffset CreatedAt);

public sealed record AppliedFix(
    string Id,
    string Path,
    string BackupPath,
    string Issue,
    DateTimeOffset AppliedAt);

public sealed record RestoredFix(string Path, string BackupPath, DateTimeOffset RestoredAt);
