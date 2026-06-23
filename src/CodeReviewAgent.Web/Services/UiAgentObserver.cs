using CodeReviewAgent.Core.Abstractions;
using CodeReviewAgent.Web.Models;

namespace CodeReviewAgent.Web.Services;

/// <summary>
/// 把 Core 的同步观察者契约转换为 UI 事件。组件负责把回调重新调度到自己的
/// Blazor 渲染上下文，因此 Core 依然完全不依赖 Web。
/// </summary>
public sealed class UiAgentObserver : IAgentObserver, IDisposable
{
    private readonly Action<AgentTraceEvent> _publish;
    private int _disposed;

    public UiAgentObserver(Action<AgentTraceEvent> publish)
    {
        _publish = publish ?? throw new ArgumentNullException(nameof(publish));
    }

    public void OnStep(string agentName, int step) =>
        Publish(agentName, "step", $"第 {step + 1} 步", string.Empty);

    public void OnThought(string agentName, string thought) =>
        Publish(agentName, "thought", "模型输出", thought);

    public void OnAction(string agentName, string toolName, string argumentsJson) =>
        Publish(agentName, "action", toolName, argumentsJson);

    public void OnObservation(string agentName, string toolName, string result) =>
        Publish(agentName, "observation", toolName, result);

    public void OnFinalAnswer(string agentName, string answer) =>
        Publish(agentName, "final", "最终回答", answer);

    public void OnToken(string agentName, string token) =>
        Publish(agentName, "token", "流式片段", token);

    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    private void Publish(string agent, string kind, string title, string content)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        try
        {
            _publish(new AgentTraceEvent(agent, kind, title, content, DateTimeOffset.Now));
        }
        catch
        {
            // 可观测性失败不应影响真实审查任务；页面释放后尤其如此。
        }
    }
}
