# CodeReviewAgent · 基于 .NET 的代码审查 AI Agent

一个面向 C#/.NET 的 AI 代码审查 Agent。它使用手写 ReAct 循环驱动工具调用，并可按需触发风格、安全、性能三个专家并行的深度审查工作流。

Web 是面向用户的工作台；MCP 是面向其它 AI 客户端的标准接口。两者只依赖 `CodeReviewAgent.Core`，不会相互调用。

## 功能概览

- 手写 ReAct：Thought → Action → Observation，带最大步数保护和可观测事件。
- 6 个内部工具：文件浏览、读取、Roslyn 分析、规范检索、修复建议、编译验证。
- 多 Agent 深度审查：风格/安全/性能专家并行，再由主审汇总。
- RAG：从 `knowledge/` 中检索编码规范。
- Web 工作台：上传 `.cs`、选择目录、多轮聊天、Markdown 审查报告、完整工具轨迹。
- MCP：`analyze_csharp`（离线静态分析）和 `review_directory`（需 LLM）。

## 环境要求

- .NET SDK 8.0+，并安装对应的 .NET 8 运行时。
- 一个 OpenAI 兼容的 LLM 端点与 API Key（DeepSeek、通义、硅基流动、本地 Ollama 等）。
- MCP Inspector 验证可选，需要 Node.js 18+。

## 配置

每个可运行项目（`Web` / `Mcp`）均按下列优先级读取配置：

1. `appsettings.json`：非机密默认值，提交到仓库；
2. `appsettings.Local.json`：本地机密，不提交；
3. 环境变量：最高优先级，例如 `Llm__ApiKey`。

复制模板并填入 Key：

```bash
cp src/CodeReviewAgent.Web/appsettings.Local.json.example src/CodeReviewAgent.Web/appsettings.Local.json
cp src/CodeReviewAgent.Mcp/appsettings.Local.json.example src/CodeReviewAgent.Mcp/appsettings.Local.json
```

### 审查目录权限

`ReviewAccess` 同时约束 Web 与 MCP：

```json
"ReviewAccess": {
  "AllowArbitraryDirectories": true,
  "SharedRoots": []
}
```

- 课程开发环境可保留 `AllowArbitraryDirectories: true`，允许本机或共享目录的绝对路径。
- 部署到多人环境时应改为 `false`，并只配置可信共享根目录，例如 `"SharedRoots": ["/srv/reviews"]`。
- Web 中的外部目录始终默认只读。必须由用户显式勾选“允许修改当前目录”后，Agent 才能获得修复工具；修复工具会在写入前创建 `.bak` 备份。
- 上传的 `.cs` 文件会进入会话隔离工作区，不会修改原始上传位置。

## 构建与测试

```bash
dotnet restore
dotnet build CodeReviewAgent.sln --no-restore
dotnet test tests/CodeReviewAgent.Tests/CodeReviewAgent.Tests.csproj --no-restore
```

## 启动 Web 工作台

```bash
dotnet run --project src/CodeReviewAgent.Web
```

浏览器打开启动日志给出的地址。建议演示流程：

1. 上传 `samples/OrderService.cs` 或输入待审查目录；
2. 点击“全面深度审查”；
3. 在右侧查看专家、工具参数和 Observation；
4. 审查确认后，显式开启写入权限，再请求修复并编译验证。

## 使用 MCP

启动 stdio 服务：

```bash
dotnet run --project src/CodeReviewAgent.Mcp
```

注意：MCP 的 stdout 是 JSON-RPC 协议通道，日志只会写入 stderr。可用 Inspector 验证，无需连接外部 AI：

```bash
npx @modelcontextprotocol/inspector dotnet run --project src/CodeReviewAgent.Mcp
```

在 Inspector 中执行：

1. `tools/list`，应看到 `analyze_csharp` 和 `review_directory`；
2. 调用 `analyze_csharp`，参数为 `samples/OrderService.cs` 的绝对路径；该步骤不需要 API Key；
3. 配置 Key 后调用 `review_directory`，参数为待审查目录的绝对路径。

MCP 目前只暴露只读能力；修复操作必须在 Web 中经过显式写入授权。

## 项目结构

```text
src/CodeReviewAgent.Core/  ReAct、记忆、工具、RAG、多 Agent 编排
src/CodeReviewAgent.Web/   Blazor Server 工作台
src/CodeReviewAgent.Mcp/   MCP stdio 服务端
tests/                     xUnit 离线测试
knowledge/                 RAG 规范语料
samples/                   演示样例
docs/                      架构与反思报告
```

详细设计见 [架构文档](docs/architecture.md) 与 [开发计划](docs/开发计划.md)。
