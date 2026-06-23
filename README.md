# CodeReviewAgent · 基于 .NET 的代码审查 AI Agent

一个面向 C#/.NET 的 AI 代码审查 Agent。系统以手写 ReAct 循环驱动工具调用，并可按需触发风格、安全、性能三个专家并行的深度审查工作流。

Web 是面向用户的工作台，MCP 是面向其它 AI 客户端的标准接口；Console 是额外的命令行演示入口。三个入口都只依赖 `CodeReviewAgent.Core`，彼此不互调。

## 解决方案结构

```text
src/CodeReviewAgent.Core/     ReAct、记忆、工具、RAG、多 Agent 编排
src/CodeReviewAgent.Console/  命令行对话入口
src/CodeReviewAgent.Web/      Blazor 审查工作台
src/CodeReviewAgent.Mcp/      MCP stdio 服务端
tests/CodeReviewAgent.Tests/  xUnit 离线测试
knowledge/                    RAG 规范语料
samples/                      演示样例
docs/                         架构、反思报告与开发计划
```

## 环境要求

- .NET SDK 8.0+，并安装 .NET 8 Runtime；
- 一个 OpenAI 兼容的 LLM API Key；
- 可选：Node.js 18+，用于 MCP Inspector。

## 配置

每个可运行项目按以下优先级读取配置：`appsettings.json` → `appsettings.Local.json` → 环境变量。不要提交真实 Key。

复制要运行项目对应的模板，例如 Web：

```bash
cp src/CodeReviewAgent.Web/appsettings.Local.json.example src/CodeReviewAgent.Web/appsettings.Local.json
```

然后填入 `Llm:ApiKey`、`Llm:Endpoint` 与 `Llm:ChatModel`。Console、MCP 也各自需要一份同目录的 `appsettings.Local.json`；或者使用环境变量 `Llm__ApiKey`。

## 构建与测试

```bash
dotnet restore
dotnet build CodeReviewAgent.sln --no-restore
dotnet test tests/CodeReviewAgent.Tests/CodeReviewAgent.Tests.csproj --no-restore
```

## 启动入口

```bash
dotnet run --project src/CodeReviewAgent.Web
dotnet run --project src/CodeReviewAgent.Console -- samples
dotnet run --project src/CodeReviewAgent.Mcp
```

> **VSCode 集成终端启动 Console 注意**：在 VSCode 集成终端里运行命令行版时，中文输入可能乱码（ConPTY 伪终端的代码页限制）。先执行 `chcp 65001` 再 `dotnet run`，同一会话内即可正常输入中文；Windows 系统自带终端（conhost）无需此步。

MCP 是 stdio 服务，直接运行后会等待客户端输入；用 Inspector 验证：

```bash
npx @modelcontextprotocol/inspector dotnet run --project src/CodeReviewAgent.Mcp
```

## Web 修复流程

- “上传副本”会将 `.cs` 文件复制到隔离工作区，绝不修改原文件；修复完成后可下载当前副本结果；
- “使用目录”用于原地审查本机或共享目录；外部目录默认只读；
- 开启“允许修改当前目录”后，Agent 只能先生成待确认 diff；用户确认后才会写入，并自动创建 `.bak`；
- 页面支持查看 `.bak`、单文件回滚，以及确认写入后的编译验证。

## 审查目录权限

`ReviewAccess` 同时约束 Web 与 MCP：

```json
"ReviewAccess": {
  "AllowArbitraryDirectories": true,
  "SharedRoots": []
}
```

课程开发环境可保留任意目录模式；部署到共享环境时请改为 `false`，并列出可信根目录，例如 `"SharedRoots": ["/srv/reviews"]`。

详细设计见 [架构文档](docs/architecture.md)、[反思报告](docs/反思报告.md) 与 [开发计划](docs/开发计划.md)。
