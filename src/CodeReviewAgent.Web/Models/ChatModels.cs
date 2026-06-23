namespace CodeReviewAgent.Web.Models;

/// <summary>页面当前生命周期内保留的一条聊天消息。</summary>
public sealed record ChatMessageViewModel(
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    bool IsError = false);

/// <summary>可观测性面板中的一条 Agent 事件。</summary>
public sealed record AgentTraceEvent(
    string Agent,
    string Kind,
    string Title,
    string Content,
    DateTimeOffset CreatedAt);
