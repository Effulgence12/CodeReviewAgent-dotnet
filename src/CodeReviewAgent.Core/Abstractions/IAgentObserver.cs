namespace CodeReviewAgent.Core.Abstractions;

/// <summary>
/// 推理过程观察者：ReAct 循环在每一步把 Thought / Action / Observation
/// 事件推给前端，实现「Agent 行为可观测性」（课程加分项）。
/// 前端（Web）实现此接口即可实时渲染推理轨迹；不关心轨迹的场景
/// （单元测试、MCP）可用空实现 <see cref="NullAgentObserver"/>。
///
/// 设计要点：Core 不直接依赖任何 UI，只依赖这个抽象 —— 这是「依赖倒置（DIP）」，
/// 也是 A、C 能并行开发的关键边界：A 在循环里调用本接口，C 去实现它。
/// </summary>
public interface IAgentObserver
{
    /// <summary>进入第 step 步推理（从 0 计）。</summary>
    void OnStep(string agentName, int step);

    /// <summary>模型产出的思考 / 中间文本（可能为空，纯工具调用步常为空）。</summary>
    void OnThought(string agentName, string thought);

    /// <summary>模型决定调用某个工具及其参数（JSON 字符串）。</summary>
    void OnAction(string agentName, string toolName, string argumentsJson);

    /// <summary>工具真实执行后的返回结果（Observation）。</summary>
    void OnObservation(string agentName, string toolName, string result);

    /// <summary>Agent 得出最终答案。</summary>
    void OnFinalAnswer(string agentName, string answer);

    /// <summary>流式 token 增量（仅流式模式触发）。</summary>
    void OnToken(string agentName, string token);
}

/// <summary>空观察者：用于不关心推理轨迹的场景（单元测试、MCP 服务端）。</summary>
public sealed class NullAgentObserver : IAgentObserver
{
    /// <summary>全局共享的单例，避免重复分配。</summary>
    public static readonly NullAgentObserver Instance = new();

    public void OnStep(string agentName, int step) { }
    public void OnThought(string agentName, string thought) { }
    public void OnAction(string agentName, string toolName, string argumentsJson) { }
    public void OnObservation(string agentName, string toolName, string result) { }
    public void OnFinalAnswer(string agentName, string answer) { }
    public void OnToken(string agentName, string token) { }
}
