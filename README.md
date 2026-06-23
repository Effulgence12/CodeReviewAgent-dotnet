# CodeReviewAgent · 基于 .NET 的代码审查 AI Agent

.NET 程序设计课程期末项目 —— 一个能够自主推理、规划并调用工具完成 C#/.NET 代码审查的 AI Agent。

核心是一段手写的 ReAct 推理循环（Thought → Action → Observation），由真实大模型驱动决策；以一个对话式编排 Agent 为唯一入口，按需触发多 Agent 深度审查（评审 → 汇总 → 修复验证三阶段）。对外提供 Web 聊天界面，并通过 MCP 服务端把能力暴露给其它 AI 客户端。

> 当前核心引擎（组员 A）以及工具、Roslyn 分析、修复验证和 RAG（组员 B）已落地，并提供可直接对话使用的命令行入口（Console）用于端到端演示与自测；Web 交互、完整 MCP 验证及课程文档仍按 `docs/开发计划.md` 继续开发。

## 解决方案结构

```
CodeReviewAgent-dotnet/
├─ src/
│  ├─ CodeReviewAgent.Core/     核心类库：ReAct 循环、记忆、工具、RAG、编排（不可单独运行）
│  ├─ CodeReviewAgent.Console/  命令行对话入口（Spectre.Console，实时推理轨迹 + 改动 diff）
│  ├─ CodeReviewAgent.Web/      Blazor 聊天界面
│  └─ CodeReviewAgent.Mcp/      MCP stdio 服务端（对外暴露能力）
├─ tests/CodeReviewAgent.Tests/  xUnit 单元测试
├─ knowledge/                 RAG 知识库语料（编码规范）
├─ samples/                   含缺陷的示例代码（演示/测试）
├─ docs/                      开发计划、架构文档、反思报告
└─ README.md
```

依赖方向单一：`Console`、`Web`、`Mcp` 均只依赖 `Core`，彼此不互调。

## 环境要求

- .NET SDK 8.0+（项目目标 `net8.0`）
- 一个 OpenAI 兼容的 LLM 端点与 API Key（DeepSeek / 通义 / 硅基流动 / 本地 Ollama 等均可）

## 配置（填入你的 API Key）

配置分层：`appsettings.json`（非机密默认值，提交）→ `appsettings.Local.json`（本地机密，gitignore，不提交）→ 环境变量（最高优先级）。密钥不写入源码、不进版本库。

在要运行的项目目录下复制模板并填 Key（以 Console 为例）：

```powershell
copy src\CodeReviewAgent.Console\appsettings.Local.json.example src\CodeReviewAgent.Console\appsettings.Local.json
# 然后编辑该文件，把 "在此填入你的key" 改成真实 Key
```

每个可运行项目（Console / Web / Mcp）各读取本目录下的 `appsettings.Local.json`；要运行哪个就在哪个目录放一份。也可用环境变量 `Llm__ApiKey` 覆盖。

## 构建与运行

```bash
dotnet build                                       # 还原并编译整个解决方案
dotnet run --project src/CodeReviewAgent.Console    # 启动命令行对话（可选：-- <审查目录>，默认 samples）
dotnet run --project src/CodeReviewAgent.Web        # 启动 Web 聊天界面
dotnet test                                        # 运行单元测试
```

> **VSCode 集成终端启动注意**：在 VSCode 的集成终端里运行命令行版时，中文输入可能乱码
> （ConPTY 伪终端的代码页限制）。请先执行 `chcp 65001` 再启动，同一会话内即可正常输入中文：
> ```powershell
> chcp 65001
> dotnet run --project src/CodeReviewAgent.Console
> ```
> Windows 系统自带终端（conhost）无需此步。

命令行示例（启动后直接输入）：

```
列出文件并分析 OrderService.cs 有哪些问题
全面审查一遍这个项目          # 触发多专家（风格/安全/性能）并行深度审查
把空 catch 问题修掉并验证能编译   # 触发改代码 + 编译验证，并显示新旧 diff
```

