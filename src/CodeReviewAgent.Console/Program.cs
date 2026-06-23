using CodeReviewAgent.Console;
using CodeReviewAgent.Core;
using CodeReviewAgent.Core.Agent;
using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Spectre.Console;

// 先用 Win32 API 把控制台代码页切到 UTF-8（绕过 Console.InputEncoding 在 ConPTY 下的失败），
// 这样 VSCode 集成终端键入的中文会以 UTF-8 字节进入 stdin。
NativeConsole.TryUseUtf8();
// 输出：再尽量切到 UTF-8（失败则保持默认，不影响渲染）。
try { System.Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { /* 终端不支持则保持默认 */ }
// 输入：用显式 UTF-8 StreamReader 直接读标准输入流（不依赖 Console.InputEncoding），
// 配合上面的代码页设置，对 conhost、VSCode 集成终端、管道重定向都能正确读中文。
using var stdin = new StreamReader(System.Console.OpenStandardInput(), new System.Text.UTF8Encoding(false));

// ── 配置（.NET 惯用分层）：appsettings.json（非机密默认值，随程序输出）
//    → appsettings.Local.json（本地机密覆盖，gitignore，不进版本库）→ 环境变量（部署/CI 覆盖）──
var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables();

builder.Services.AddCodeReviewAgent(builder.Configuration);
using var host = builder.Build();

var factory = host.Services.GetRequiredService<ConversationalAgentFactory>();
var llm = host.Services.GetRequiredService<KernelFactory>().Llm;

// ── 解析审查根目录（首个命令行参数，默认 samples）。相对路径用 DirectoryLocator 向上查找，
//    使其不依赖 `dotnet run` 把工作目录设到项目目录的细节。──
var target = args.FirstOrDefault(a => !a.StartsWith('-')) ?? "samples";
var reviewRoot = Path.IsPathRooted(target)
    ? target
    : DirectoryLocator.Resolve(target) ?? Path.GetFullPath(target);

AnsiConsole.Write(new FigletText("Code Review Agent").Color(Spectre.Console.Color.Aqua));
AnsiConsole.Write(new Panel(new Markup(
        $"[grey]模型[/]    {Markup.Escape(llm.ChatModel)}\n" +
        $"[grey]端点[/]    {Markup.Escape(llm.Endpoint)}\n" +
        $"[grey]审查根[/]  {Markup.Escape(reviewRoot)}"))
    .Header("[aqua]运行配置[/]").BorderColor(Spectre.Console.Color.Grey));

// 缺 Key 显式报错（真实状态，绝不伪造数据冒充）。
if (!llm.IsConfigured)
{
    AnsiConsole.MarkupLine(
        "[red]✗ 未检测到 LLM ApiKey。[/]请复制 appsettings.Local.json.example 为 appsettings.Local.json " +
        "并填入 Llm:ApiKey，或设置环境变量 Llm__ApiKey。");
    return 1;
}
if (!Directory.Exists(reviewRoot))
{
    AnsiConsole.MarkupLine($"[red]✗ 审查目录不存在：[/]{Markup.Escape(reviewRoot)}");
    return 1;
}

AnsiConsole.MarkupLine(
    "\n[grey]这是一个对话式代码审查助手。它会自行决定调用工具（浏览/读取/静态分析/检索规范/修改/编译），\n" +
    "需要时还会发起多专家深度审查。可直接提问，例如：[/]");
AnsiConsole.MarkupLine("  [aqua]•[/] 列出目录里的文件，分析 OrderService.cs 有哪些问题");
AnsiConsole.MarkupLine("  [aqua]•[/] 全面审查一遍这个项目");
AnsiConsole.MarkupLine("  [aqua]•[/] 把你发现的空 catch 问题修掉并验证能编译");
AnsiConsole.MarkupLine("[grey]输入 [/][aqua]exit[/][grey] 退出。[/]\n");

// 每个会话一个长生命周期对话 Agent；多轮对话间记忆自动累积。
var observer = new SpectreAgentObserver(Path.GetFullPath(reviewRoot));
var agent = factory.Create(reviewRoot, observer);

while (true)
{
    // 用显式 UTF-8 StreamReader 读取（而非 AnsiConsole.Ask），既支持重定向/非交互，
    // 又避免 VSCode 集成终端下的中文输入乱码。
    AnsiConsole.Markup("[aqua]你 ›[/] ");
    var input = stdin.ReadLine();
    if (input is null ||
        string.IsNullOrWhiteSpace(input) ||
        input.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    AnsiConsole.Write(new Rule("[aqua]推理轨迹[/]").LeftJustified());
    try
    {
        var result = await agent.RunAsync(input);

        AnsiConsole.Write(new Rule("[green]回答[/]").LeftJustified());
        AnsiConsole.Write(new Panel(new Markup(Markup.Escape(result.Answer)))
            .BorderColor(Spectre.Console.Color.Green).Expand());
        AnsiConsole.MarkupLine(
            $"[grey]步数 {result.StepsUsed} · 工具调用 {result.ToolCallCount} 次 · " +
            $"{(result.Completed ? "已完成" : "达到最大步数")}[/]\n");
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[red]运行出错：[/]{Markup.Escape(ex.Message)}\n");
    }
}

return 0;
