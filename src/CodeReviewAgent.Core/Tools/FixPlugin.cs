using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Tools;

/// <summary>
/// 代码修改工具（组员 B 负责）：本项目「读 → 改 → 验」闭环里的「改」。
/// 这是少数有「写」能力的工具，安全护栏是重点：仅允许修改审查根目录内的文件，
/// 写入前应备份原文件以便回滚，避免误改/越权。
/// </summary>
public sealed class FixPlugin
{
    private readonly SandboxedPathResolver _paths;
    private readonly Action<string, string> _replaceFile;

    public FixPlugin(string root)
        : this(root, (temporaryPath, destinationPath) =>
            File.Move(temporaryPath, destinationPath, overwrite: true))
    {
    }

    internal FixPlugin(string root, Action<string, string> replaceFile)
    {
        _paths = new SandboxedPathResolver(root);
        _replaceFile = replaceFile ?? throw new ArgumentNullException(nameof(replaceFile));
    }

    [KernelFunction("propose_fix")]
    [Description("针对指定文件的某个问题，生成并写入修改（改写后的代码）。仅限审查目录内，写入前自动备份以便回滚。返回修改摘要。")]
    public string ProposeFix(
        [Description("相对审查根目录的 .cs 文件路径")] string path,
        [Description("要修复的问题描述（含大致位置）")] string issue,
        [Description("修改后的完整文件内容")] string newContent)
    {
        if (string.IsNullOrWhiteSpace(newContent))
        {
            throw new ArgumentException("修改后的文件内容不能为空。", nameof(newContent));
        }
        if (string.IsNullOrWhiteSpace(issue))
        {
            throw new ArgumentException("问题描述不能为空。", nameof(issue));
        }

        var sourcePath = _paths.ResolveExistingCSharpFile(path);
        var backupPath = EnsureBackup(path);
        var temporaryPath = Path.Combine(
            Path.GetDirectoryName(sourcePath)!,
            $".{Path.GetFileName(sourcePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(temporaryPath, newContent, new System.Text.UTF8Encoding(false));
            _replaceFile(temporaryPath, sourcePath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return $"已更新 {_paths.ToRelativeDisplayPath(sourcePath)}；问题：{issue}；" +
               $"原始备份：{_paths.ToRelativeDisplayPath(backupPath)}";
    }

    /// <summary>Creates the immutable first-version backup and returns its full path.</summary>
    internal string EnsureBackup(string path)
    {
        var sourcePath = _paths.ResolveExistingCSharpFile(path);
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
}
