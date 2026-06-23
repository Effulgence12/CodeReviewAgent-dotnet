using CodeReviewAgent.Core.Configuration;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Web.Services;

/// <summary>上传会话工作区的 Web 专用配置。</summary>
public sealed class ReviewWorkspaceOptions
{
    public string UploadRoot { get; set; } = "App_Data/reviews";
    public long MaxUploadBytes { get; set; } = 2 * 1024 * 1024;
    public int MaxFilesPerUpload { get; set; } = 20;
}

/// <summary>
/// 为浏览器上传创建隔离目录，并把用户选择的服务器目录交由 Core 的统一权限策略验证。
/// 上传目录属于当前浏览器会话，默认允许写入；外部目录是否写入由页面显式授权决定。
/// </summary>
public sealed class ReviewWorkspaceService
{
    private readonly string _uploadRoot;
    private readonly ReviewWorkspaceOptions _options;
    private readonly ReviewDirectoryPolicy _directoryPolicy;

    public ReviewWorkspaceService(
        IHostEnvironment environment,
        IOptions<ReviewWorkspaceOptions> options,
        ReviewDirectoryPolicy directoryPolicy)
    {
        _options = options.Value;
        _directoryPolicy = directoryPolicy;
        _uploadRoot = Path.GetFullPath(Path.IsPathRooted(_options.UploadRoot)
            ? _options.UploadRoot
            : Path.Combine(environment.ContentRootPath, _options.UploadRoot));
    }

    public string CreateSessionWorkspace()
    {
        var workspace = Path.Combine(_uploadRoot, $"session-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        return workspace;
    }

    public int MaxFilesPerUpload => _options.MaxFilesPerUpload;

    public string ResolveExistingDirectory(string path) => _directoryPolicy.ResolveDirectory(path);

    public async Task<UploadResult> SaveUploadsAsync(
        IReadOnlyList<IBrowserFile> files,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
        {
            throw new ArgumentException("请至少选择一个 .cs 文件。", nameof(files));
        }
        if (files.Count > _options.MaxFilesPerUpload)
        {
            throw new InvalidOperationException($"一次最多上传 {_options.MaxFilesPerUpload} 个文件。 ");
        }

        var workspace = CreateSessionWorkspace();
        var savedNames = new List<string>();
        try
        {
            foreach (var file in files)
            {
                if (!string.Equals(Path.GetExtension(file.Name), ".cs", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"只允许上传 .cs 文件：{file.Name}");
                }
                if (file.Size <= 0 || file.Size > _options.MaxUploadBytes)
                {
                    throw new InvalidOperationException(
                        $"文件 {file.Name} 必须介于 1 字节和 {_options.MaxUploadBytes / 1024 / 1024} MB 之间。 ");
                }

                var fileName = Path.GetFileName(file.Name);
                var destination = GetUniquePath(workspace, fileName);
                await using var source = file.OpenReadStream(_options.MaxUploadBytes, ct);
                await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await source.CopyToAsync(target, ct);
                savedNames.Add(Path.GetFileName(destination));
            }

            return new UploadResult(workspace, savedNames);
        }
        catch
        {
            TryDeleteWorkspace(workspace);
            throw;
        }
    }

    public void TryDeleteWorkspace(string? workspace)
    {
        if (string.IsNullOrWhiteSpace(workspace))
        {
            return;
        }

        var fullPath = Path.GetFullPath(workspace);
        if (!fullPath.StartsWith(_uploadRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // 文件仍被读取时留待下次应用重启/人工清理，不能影响用户关闭页面。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        var sequence = 1;
        while (File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{stem}-{sequence++}{extension}");
        }
        return candidate;
    }
}

public sealed record UploadResult(string WorkspacePath, IReadOnlyList<string> FileNames);
