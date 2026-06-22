namespace CodeReviewAgent.Core.Configuration;

/// <summary>
/// 解析相对数据目录（如 knowledge / samples），使其不依赖进程的当前工作目录。
/// `dotnet run --project` 会把工作目录设为项目目录，导致 "knowledge" 等相对路径失效；
/// 这里从「当前目录」与「程序所在目录」分别向上逐级查找同名目录，返回首个存在者。
/// </summary>
public static class DirectoryLocator
{
    /// <summary>解析目录；找不到返回 null。</summary>
    public static string? Resolve(string relativeOrName)
    {
        if (string.IsNullOrWhiteSpace(relativeOrName))
        {
            return null;
        }
        // 绝对路径或相对当前目录已存在，直接用。
        if (Path.IsPathRooted(relativeOrName) && Directory.Exists(relativeOrName))
        {
            return relativeOrName;
        }
        if (Directory.Exists(relativeOrName))
        {
            return Path.GetFullPath(relativeOrName);
        }

        // 取末段目录名，分别从「当前工作目录」与「程序所在目录」向上逐级查找。
        var name = Path.GetFileName(relativeOrName.TrimEnd('/', '\\'));
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var candidate = Path.Combine(dir.FullName, name);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
        }
        return null;
    }
}
