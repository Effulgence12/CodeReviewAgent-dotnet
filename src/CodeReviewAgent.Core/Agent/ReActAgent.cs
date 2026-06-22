using System.Text;
using CodeReviewAgent.Core.Abstractions;
using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Memory;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace CodeReviewAgent.Core.Agent;

/// <summary>
/// 手写的 ReAct（Reasoning + Acting）推理循环 —— 本项目的核心，答辩需逐行讲解。
///
/// 关键设计：使用 Semantic Kernel 的【手动函数调用】模式
/// （<c>FunctionChoiceBehavior.Auto(autoInvoke: false)</c>）。SK 只负责两件事：
/// 把工具描述成 JSON Schema 交给模型、解析模型返回的工具调用请求；而
/// 「是否继续、执行哪个工具、如何把结果回灌上下文、何时停止」这套循环完全由本类掌控。
/// 这样既复用了框架的工程红利，又让推理循环的每一行都属于我们、可在答辩中逐行讲解，
/// 不会被高层封装黑盒化。
///
/// 一轮迭代 = Thought（模型思考）→ Action（调用工具）→ Observation（工具结果回灌），
/// 直到模型不再请求工具（得出最终答案）或触达 <see cref="ReActOptions.MaxSteps"/>。
///
/// 多轮对话：同一个实例的 <see cref="RunAsync"/> 可被多次调用，记忆在多轮间累积。
/// </summary>
public sealed class ReActAgent
{
    private readonly Kernel _kernel;
    private readonly IChatCompletionService _chat;
    private readonly ConversationMemory _memory;
    private readonly ReActOptions _options;
    private readonly LlmOptions _llm;
    private readonly IAgentObserver _observer;
    private readonly string _name;

    /// <param name="name">Agent 名称（用于可观测性区分，如「安全专家」「对话助手」）。</param>
    /// <param name="kernel">已挂载工具插件的 SK Kernel。</param>
    /// <param name="memory">对话/工作记忆（多轮间复用同一实例即获得记忆）。</param>
    /// <param name="options">循环参数（最大步数、是否流式）。</param>
    /// <param name="llm">采样参数（温度、最大 token、模型名）。</param>
    /// <param name="observer">推理过程观察者；为空则不上报。</param>
    public ReActAgent(
        string name,
        Kernel kernel,
        ConversationMemory memory,
        ReActOptions options,
        LlmOptions llm,
        IAgentObserver? observer = null)
    {
        _name = name;
        _kernel = kernel;
        _chat = kernel.GetRequiredService<IChatCompletionService>();
        _memory = memory;
        _options = options;
        _llm = llm;
        _observer = observer ?? NullAgentObserver.Instance;
    }

    /// <summary>运行一轮任务/对话；返回最终答案与统计。</summary>
    public async Task<AgentResult> RunAsync(string goal, CancellationToken ct = default)
    {
        // 把本轮用户输入加入记忆（多轮对话时，历史已含此前所有上下文）。
        _memory.AddUserMessage(goal);

        // autoInvoke:false 是关键 —— 让 SK 把工具调用请求交回给我们，循环归我们掌控。
        var settings = new OpenAIPromptExecutionSettings
        {
            FunctionChoiceBehavior = FunctionChoiceBehavior.Auto(autoInvoke: false),
            Temperature = _llm.Temperature,
            MaxTokens = _llm.MaxTokens,
        };

        int toolCallCount = 0;

        for (int step = 0; step < _options.MaxSteps; step++)
        {
            _observer.OnStep(_name, step);

            // ===== 1. Thought：调用 LLM，获取下一步（可能含工具调用请求）=====
            var (assistantMessage, functionCalls, content) =
                await GetNextStepAsync(settings, ct);
            _memory.Add(assistantMessage);

            if (!string.IsNullOrWhiteSpace(content))
            {
                _observer.OnThought(_name, content);
            }

            // ===== 2. 若模型未请求任何工具 → 任务完成，返回最终答案 =====
            if (functionCalls.Count == 0)
            {
                _observer.OnFinalAnswer(_name, content);
                return AgentResult.Done(content, step + 1, toolCallCount);
            }

            // ===== 3. Action + Observation：真实执行每个工具并把结果回灌上下文 =====
            foreach (var call in functionCalls)
            {
                toolCallCount++;
                _observer.OnAction(_name, call.FunctionName, ArgumentsToJson(call));
                FunctionResultContent resultContent;
                try
                {
                    resultContent = await call.InvokeAsync(_kernel, ct);
                }
                catch (Exception ex)
                {
                    // 工具失败也作为 Observation 回灌，让模型有机会改用其他工具/参数，而非整体崩溃。
                    resultContent = new FunctionResultContent(call, $"工具执行失败：{ex.Message}");
                }
                _memory.Add(resultContent.ToChatMessage());
                _observer.OnObservation(_name, call.FunctionName, Stringify(resultContent.Result));
            }
            // 循环回到第 1 步，模型基于新的 Observation 继续推理。
        }

        // ===== 4. 步数保护：达到上限仍未收敛，避免无限循环 =====
        return AgentResult.MaxStepsReached(string.Empty, _options.MaxSteps, toolCallCount);
    }

    /// <summary>
    /// 调用一次 LLM 取下一步。流式模式下边产出 token 边用
    /// <see cref="FunctionCallContentBuilder"/> 拼装工具调用；非流式则直接获取。
    /// </summary>
    private async Task<(ChatMessageContent Message, IReadOnlyList<FunctionCallContent> Calls, string Content)>
        GetNextStepAsync(OpenAIPromptExecutionSettings settings, CancellationToken ct)
    {
        if (_options.Streaming)
        {
            try
            {
                return await StreamNextStepAsync(settings, ct);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // 某些兼容端点的 SSE 流式与 OpenAI SDK 的流式函数调用装配器不兼容，
                // 回退到非流式。回退结果同样是真实 LLM 调用，绝非假数据。
                _observer.OnThought(_name, "[流式不可用，已自动回退到非流式]");
            }
        }

        var response = await _chat.GetChatMessageContentAsync(_memory.History, settings, _kernel, ct);
        var directCalls = FunctionCallContent.GetFunctionCalls(response).ToList();
        return (response, directCalls, response.Content ?? string.Empty);
    }

    /// <summary>流式获取下一步：逐 token 上报，同时增量拼装工具调用项。</summary>
    private async Task<(ChatMessageContent Message, IReadOnlyList<FunctionCallContent> Calls, string Content)>
        StreamNextStepAsync(OpenAIPromptExecutionSettings settings, CancellationToken ct)
    {
        var contentBuilder = new StringBuilder();
        var callBuilder = new FunctionCallContentBuilder();
        AuthorRole? role = null;

        await foreach (var update in _chat.GetStreamingChatMessageContentsAsync(_memory.History, settings, _kernel, ct))
        {
            if (!string.IsNullOrEmpty(update.Content))
            {
                contentBuilder.Append(update.Content);
                _observer.OnToken(_name, update.Content);
            }
            role ??= update.Role;
            callBuilder.Append(update);
        }

        var content = contentBuilder.ToString();
        var calls = callBuilder.Build();

        // 把流式增量重新组装成一条完整的 assistant 消息，连同工具调用项写回历史。
        var message = new ChatMessageContent(role ?? AuthorRole.Assistant, content)
        {
            ModelId = _llm.ChatModel,
        };
        foreach (var call in calls)
        {
            message.Items.Add(call);
        }
        return (message, calls, content);
    }

    /// <summary>把工具调用参数序列化为 JSON，供可观测性展示。</summary>
    private static string ArgumentsToJson(FunctionCallContent call)
    {
        if (call.Arguments is null || call.Arguments.Count == 0)
        {
            return "{}";
        }
        return System.Text.Json.JsonSerializer.Serialize(call.Arguments);
    }

    /// <summary>把工具返回值转成字符串，供可观测性展示。</summary>
    private static string Stringify(object? result) =>
        result switch
        {
            null => string.Empty,
            string s => s,
            _ => result.ToString() ?? string.Empty,
        };
}
