namespace CodeReviewAgent.Core.Orchestration;

/// <summary>单个专家 Agent 的审查产出。</summary>
public sealed record SpecialistReview(string Role, string Markdown, int Steps, int ToolCalls);

/// <summary>一次完整的多 Agent 深度审查结果（评审 + 汇总）。</summary>
public sealed record ReviewResult(
    string TargetDirectory,
    IReadOnlyList<SpecialistReview> Specialists,
    string FinalReport,
    TimeSpan Elapsed)
{
    /// <summary>所有专家的工具调用次数之和（可观测性统计）。</summary>
    public int TotalToolCalls => Specialists.Sum(s => s.ToolCalls);
}

/// <summary>一次修复阶段的结果（深度审查阶段3，可选）。</summary>
public sealed record RemediationResult(
    string TargetDirectory,
    string Report,
    int StepsUsed,
    int ToolCalls,
    TimeSpan Elapsed);
