using System.ComponentModel;
using Microsoft.SemanticKernel;

namespace CodeReviewAgent.Core.Tools;

/// <summary>
/// 文件系统工具（组员 B 负责）：让 Agent 能浏览与读取待审查代码。
/// 沙箱：所有路径都相对构造时传入的 <c>root</c>，实现时需防目录穿越（禁止访问 root 之外）。
/// </summary>
public sealed class FileSystemPlugin
{
    private readonly string _root;

    /// <param name="root">审查根目录；本插件所有访问都限定在此目录内。</param>
    public FileSystemPlugin(string root)
    {
        _root = Path.GetFullPath(root);
    }

    [KernelFunction("list_files")]
    [Description("列出审查目录下的源代码文件（递归）。返回相对路径列表，可用 pattern 过滤，如 *.cs。")]
    public string ListFiles(
        [Description("相对审查根目录的子目录，留空表示根目录")] string subDirectory = "",
        [Description("文件名通配符，如 *.cs；留空表示 *.cs")] string pattern = "*.cs")
    {
        // TODO(组员B): 递归枚举 _root/subDirectory 下匹配 pattern 的文件，返回相对路径列表；
        //             注意校验 subDirectory 不越出 _root（防目录穿越）。
        throw new NotImplementedException("FileSystemPlugin.ListFiles 待组员 B 实现。");
    }

    [KernelFunction("read_file")]
    [Description("读取指定源代码文件的内容，带行号，便于在审查中精确定位到行。")]
    public string ReadFile(
        [Description("相对审查根目录的文件路径，如 Services/Order.cs")] string path)
    {
        // TODO(组员B): 读取 _root/path 文件，逐行加行号返回；校验路径不越出 _root。
        throw new NotImplementedException("FileSystemPlugin.ReadFile 待组员 B 实现。");
    }
}
