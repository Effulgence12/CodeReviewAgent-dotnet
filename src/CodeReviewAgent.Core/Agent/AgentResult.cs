namespace CodeReviewAgent.Core.Agent;

/// <summary>
/// 一次 Agent 运行的结构化结果。
/// <paramref name="Completed"/> 为 false 表示在 MaxSteps 内未收敛（步数保护触发）。
/// </summary>
public sealed record AgentResult(
    string Answer,
    bool Completed,
    int StepsUsed,
    int ToolCallCount)
{
    /// <summary>正常完成（模型不再请求工具，给出最终答案）。</summary>
    public static AgentResult Done(string answer, int steps, int toolCalls) =>
        new(answer, true, steps, toolCalls);

    /// <summary>达到最大步数仍未完成；返回已有的部分内容或提示。</summary>
    public static AgentResult MaxStepsReached(string partial, int steps, int toolCalls) =>
        new(partial.Length == 0 ? "已达到最大推理步数限制，未能在步数内完成任务。" : partial, false, steps, toolCalls);
}
