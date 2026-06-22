using CodeReviewAgent.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// MCP stdio 服务端：stdout 是 JSON-RPC 协议通道，日志必须走 stderr，否则会破坏协议帧。
var builder = Host.CreateApplicationBuilder(args);

// 配置（.NET 惯用分层）：appsettings.json → appsettings.Local.json（本地机密，gitignore）→ 环境变量。
builder.Configuration.Sources.Clear();
builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables();

builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

// 复用 Core 的全部服务（KernelFactory / 编排器 / 知识库 …）。
builder.Services.AddCodeReviewAgent(builder.Configuration);

// 注册 MCP 服务器：stdio 传输 + 从当前程序集扫描带 [McpServerToolType] 的工具。
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
