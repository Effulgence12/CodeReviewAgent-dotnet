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
    private readonly string _root;

    public FixPlugin(string root)
    {
        _root = Path.GetFullPath(root);
    }

    [KernelFunction("propose_fix")]
    [Description("针对指定文件的某个问题，生成并写入修改（改写后的代码）。仅限审查目录内，写入前自动备份以便回滚。返回修改摘要。")]
    public string ProposeFix(
        [Description("相对审查根目录的 .cs 文件路径")] string path,
        [Description("要修复的问题描述（含大致位置）")] string issue,
        [Description("修改后的完整文件内容")] string newContent)
    {
        // TODO(组员B): 校验 path 不越出 _root → 备份原文件 → 写入 newContent → 返回修改摘要。
        //             建议保留 .bak 备份，便于回滚与答辩演示「安全写入」。
        throw new NotImplementedException("FixPlugin.ProposeFix 待组员 B 实现。");
    }
}
