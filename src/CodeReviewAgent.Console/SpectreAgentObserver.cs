using System.Text.Json;
using CodeReviewAgent.Core.Abstractions;
using Spectre.Console;

namespace CodeReviewAgent.Console;

/// <summary>
/// 把 ReAct 推理轨迹实时渲染到终端，实现「Agent 行为可观测性」。
///
/// 设计目标（对应可用性/答辩需求）：
///   1. 让用户清晰看到「谁在做什么」——同一观察者会收到对话助手、各专家、主审、修复等
///      多个 Agent 的事件（深度审查并行时还会交织），故按 Agent 名称着色 + 加锁原子输出。
///   2. 让等待可感知——顶层对话开启「流式」，思考逐 token 流出；深度审查的并行专家为非流式，
///      在每步开头显示「思考中…」提示，避免等待 LLM 时看起来像卡死。
///   3. 让工具调用一目了然——每次 Action/Observation 都带图标、工具名与（截断的）参数/结果。
///   4. 改代码可视化——侦测到 propose_fix 时，额外渲染该文件的新旧 diff（绿增 / 红删）。
/// </summary>
public sealed class SpectreAgentObserver : IAgentObserver
{
    private readonly object _lock = new();
    private readonly string _reviewRoot;

    // 正在逐 token 流式输出的 Agent 名（null 表示当前没有进行中的流式段）。
    // 用于在切换到其它事件/其它 Agent 前，给未结束的流式行补一个换行。
    private string? _streamingAgent;

    /// <param name="reviewRoot">审查根目录的绝对路径；用于在 propose_fix 时读取旧文件做 diff。</param>
    public SpectreAgentObserver(string reviewRoot) => _reviewRoot = reviewRoot;

    // 按 Agent 角色着色，让「多 Agent 协作流程」在交织输出中仍可区分来源。
    private static string Color(string agent) => agent switch
    {
        var a when a.Contains("风格") => "yellow",
        var a when a.Contains("安全") => "red",
        var a when a.Contains("性能") => "green",
        var a when a.Contains("主审") => "magenta",
        var a when a.Contains("修复") => "blue",
        _ => "aqua", // 顶层对话助手
    };

    private static string Tag(string agent) => $"[{Color(agent)}]{Markup.Escape(agent)}[/]";

    // 调用方须已持有 _lock：若有未结束的流式行，补一个换行并清空状态。
    private void FlushStreaming()
    {
        if (_streamingAgent is not null)
        {
            AnsiConsole.WriteLine();
            _streamingAgent = null;
        }
    }

    public void OnStep(string agentName, int step)
    {
        lock (_lock)
        {
            FlushStreaming();
            // 「思考中…」提示主要服务于非流式的并行专家：在等待 LLM 期间给出明确反馈。
            AnsiConsole.MarkupLine($"{Tag(agentName)} [grey]──── 第 {step + 1} 步 ────[/] [grey italic]· 思考中…[/]");
        }
    }

    public void OnThought(string agentName, string thought)
    {
        if (string.IsNullOrWhiteSpace(thought)) return;
        var preview = Truncate(thought, 320);
        lock (_lock)
        {
            FlushStreaming();
            AnsiConsole.MarkupLine($"{Tag(agentName)} [grey]💭[/] [italic]{Markup.Escape(preview)}[/]");
        }
    }

    public void OnAction(string agentName, string toolName, string argumentsJson)
    {
        lock (_lock)
        {
            FlushStreaming();
            // 粗粒度动作（触发多 Agent 子流程）单独醒目提示，让用户意识到「进入协作阶段」。
            if (toolName is "run_deep_review" or "run_remediation")
            {
                var label = toolName == "run_deep_review" ? "发起多专家深度审查（风格 / 安全 / 性能 并行）" : "发起成批修复并编译验证";
                AnsiConsole.Write(new Rule($"[bold {Color(agentName)}]▶ {Markup.Escape(label)}[/]").LeftJustified());
            }
            else
            {
                AnsiConsole.MarkupLine(
                    $"{Tag(agentName)} [grey]🔧[/] [bold]{Markup.Escape(toolName)}[/]([grey]{Markup.Escape(Truncate(argumentsJson, 160))}[/])");
            }
        }

        // propose_fix：参数里已带「修改后完整内容」，此刻文件尚未被改写，读旧文件即可做 diff。
        if (toolName == "propose_fix")
        {
            RenderProposedDiff(agentName, argumentsJson);
        }
    }

    public void OnObservation(string agentName, string toolName, string result)
    {
        var preview = Truncate(result.Replace("\n", " "), 220);
        lock (_lock)
        {
            FlushStreaming();
            AnsiConsole.MarkupLine($"{Tag(agentName)} [grey]👁  {Markup.Escape(preview)}[/]");
        }
    }

    public void OnFinalAnswer(string agentName, string answer)
    {
        lock (_lock)
        {
            FlushStreaming();
            AnsiConsole.MarkupLine($"{Tag(agentName)} [grey]✅ 给出结论（{answer.Length} 字）[/]");
        }
    }

    // 流式逐 token 输出（顶层对话/主审为流式时触发）。同一 Agent 的首个 token 先打印角色前缀，
    // 之后的 token 紧随其后；切换到其它事件时由 FlushStreaming 补换行。
    public void OnToken(string agentName, string token)
    {
        lock (_lock)
        {
            if (_streamingAgent != agentName)
            {
                FlushStreaming();
                // 注意：前缀必须是闭合的 markup —— 不能留一个未闭合的 [italic] 让后续 token 续在里面，
                // 那会被 Spectre 判为标签不配对而抛异常（曾导致流式被误判为「端点不支持」而回退）。
                AnsiConsole.Markup($"{Tag(agentName)} [grey]💭[/] ");
                _streamingAgent = agentName;
            }
            // token 是纯文本，直接用 Console.Write 输出，绕过 Spectre 的 markup 解析
            // （流式正文常含 markdown 的 [ ] 等字符，走 markup 路径易触发标签解析异常）。
            System.Console.Write(token);
        }
    }

    /// <summary>解析 propose_fix 参数，渲染该文件的新旧 diff 面板。</summary>
    private void RenderProposedDiff(string agentName, string argumentsJson)
    {
        string path;
        string newContent;
        try
        {
            using var doc = JsonDocument.Parse(argumentsJson);
            var root = doc.RootElement;
            path = root.TryGetProperty("path", out var p) ? p.GetString() ?? "" : "";
            newContent = root.TryGetProperty("newContent", out var c) ? c.GetString() ?? "" : "";
        }
        catch
        {
            return; // 参数解析失败则跳过 diff（不影响主流程）。
        }
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrEmpty(newContent)) return;

        var fullPath = Path.Combine(_reviewRoot, path);
        var oldContent = File.Exists(fullPath) ? File.ReadAllText(fullPath) : "";
        var diff = DiffRenderer.Collapse(DiffRenderer.Diff(oldContent, newContent));

        var body = new System.Text.StringBuilder();
        foreach (var line in diff)
        {
            var text = Markup.Escape(line.Text);
            body.AppendLine(line.Kind switch
            {
                DiffRenderer.LineKind.Added => $"[green]+ {text}[/]",
                DiffRenderer.LineKind.Removed => $"[red]- {text}[/]",
                DiffRenderer.LineKind.Omitted => $"[grey39]  {text}[/]",
                _ => $"[grey]  {text}[/]",
            });
        }

        lock (_lock)
        {
            FlushStreaming();
            AnsiConsole.Write(new Panel(new Markup(body.ToString().TrimEnd()))
                .Header($"[{Color(agentName)}]✎ 修改 {Markup.Escape(path)}[/]")
                .BorderColor(Spectre.Console.Color.Grey)
                .Expand());
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
