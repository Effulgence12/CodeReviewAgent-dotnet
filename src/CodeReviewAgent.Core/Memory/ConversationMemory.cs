using Microsoft.SemanticKernel.ChatCompletion;

namespace CodeReviewAgent.Core.Memory;

/// <summary>
/// 对话 / 工作记忆：封装 Semantic Kernel 的 <see cref="ChatHistory"/>，是课程要求的「记忆机制」。
/// - 短期记忆：完整的消息列表（System / User / Assistant / Tool）。
/// - 工作记忆：当前任务推进过程中累积的工具结果，同样在消息列表中。
///
/// 多轮对话的实现关键：同一个 <see cref="ConversationMemory"/> 实例在多轮之间复用，
/// 历史不断追加 —— 这就是对话 Agent「记得上文」的来源。
///
/// 当消息条数超过阈值时，对较早的历史做「滑动窗口」裁剪，避免上下文无限膨胀
/// 撑爆 token 预算。始终保留首条 System 提示与最近 N 条，确保关键指令与近期上下文不丢。
/// </summary>
public sealed class ConversationMemory
{
    private readonly int _maxMessages;

    /// <summary>底层消息历史，直接交给 LLM。</summary>
    public ChatHistory History { get; }

    /// <param name="systemPrompt">系统提示词，定义该 Agent 的角色与能力边界。</param>
    /// <param name="maxMessages">滑动窗口上限；超过则裁剪最早的普通消息。</param>
    public ConversationMemory(string systemPrompt, int maxMessages = 40)
    {
        _maxMessages = maxMessages;
        History = new ChatHistory();
        History.AddSystemMessage(systemPrompt);
    }

    /// <summary>追加一条用户消息。</summary>
    public void AddUserMessage(string content) => History.AddUserMessage(content);

    /// <summary>追加任意一条消息（assistant / tool 结果等），并触发裁剪。</summary>
    public void Add(Microsoft.SemanticKernel.ChatMessageContent message)
    {
        History.Add(message);
        Trim();
    }

    /// <summary>
    /// 滑动窗口裁剪：始终保留首条 System 消息（index 0），从 index 1 起删除最早的若干条，
    /// 直到满足阈值。只在超阈值时触发，普通审查通常不会触发。
    /// </summary>
    private void Trim()
    {
        if (History.Count <= _maxMessages)
        {
            return;
        }
        int removeCount = History.Count - _maxMessages;
        for (int i = 0; i < removeCount; i++)
        {
            History.RemoveAt(1); // index 0 是 System，跳过
        }
    }
}
