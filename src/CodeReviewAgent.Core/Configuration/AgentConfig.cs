namespace CodeReviewAgent.Core.Configuration;

/// <summary>
/// 强类型配置根。通过 Options 模式从 IConfiguration 绑定。
/// 真实值按 .NET 惯用分层覆盖：appsettings.json（默认）→ appsettings.Local.json（本地机密，gitignore）→ 环境变量。
/// </summary>
public sealed class AgentConfig
{
    public LlmOptions Llm { get; set; } = new();
    public EmbeddingOptions Embedding { get; set; } = new();
    public RagOptions Rag { get; set; } = new();
    public ReActOptions Agent { get; set; } = new();
}

/// <summary>大语言模型（OpenAI 兼容端点）配置。</summary>
public sealed class LlmOptions
{
    /// <summary>OpenAI 兼容端点地址（DashScope / DeepSeek / 硅基流动 / 本地 Ollama 等均可）。</summary>
    public string Endpoint { get; set; } = "https://dashscope.aliyuncs.com/compatible-mode/v1";
    /// <summary>API Key。机密项，不写入源码，由 appsettings.Local.json 或环境变量提供。</summary>
    public string ApiKey { get; set; } = "";
    /// <summary>对话模型名。</summary>
    public string ChatModel { get; set; } = "glm-5.1";
    /// <summary>采样温度。代码审查偏确定性，默认较低。</summary>
    public float Temperature { get; set; } = 0.2f;
    /// <summary>单次回复最大 token 数。</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>是否已配置 Key —— 缺 Key 时程序显式报错，绝不伪造数据。</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}

/// <summary>Embedding（向量化）配置。默认复用 LLM 的兼容端点与 Key。</summary>
public sealed class EmbeddingOptions
{
    /// <summary>留空则回退到 <see cref="LlmOptions.Endpoint"/>。</summary>
    public string Endpoint { get; set; } = "";
    /// <summary>留空则回退到 <see cref="LlmOptions.ApiKey"/>。</summary>
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "text-embedding-v3";
}

/// <summary>RAG 检索配置。</summary>
public sealed class RagOptions
{
    public bool Enabled { get; set; } = true;
    /// <summary>编码规范知识库目录（相对工作目录，运行时用 DirectoryLocator 向上查找）。</summary>
    public string KnowledgeDirectory { get; set; } = "knowledge";
    /// <summary>每次检索返回的最相关片段数。</summary>
    public int TopK { get; set; } = 4;
    /// <summary>语料分块大小（字符）。</summary>
    public int ChunkSize { get; set; } = 600;
    /// <summary>相邻分块的重叠（字符），避免切断语义。</summary>
    public int ChunkOverlap { get; set; } = 80;
}

/// <summary>ReAct 推理循环配置。</summary>
public sealed class ReActOptions
{
    /// <summary>单个 Agent 的最大推理步数（防止无限循环，是 ReAct 循环的安全阀）。</summary>
    public int MaxSteps { get; set; } = 12;

    /// <summary>
    /// 是否启用逐 token 流式输出。默认关闭：部分兼容端点的 SSE 流式格式与
    /// OpenAI SDK 的流式「函数调用」装配器不完全兼容；非流式同样是真实 LLM 调用。
    /// 在 OpenAI / Azure OpenAI 等端点可置为 true；流式失败时循环会自动回退到非流式。
    /// </summary>
    public bool Streaming { get; set; } = false;
}
