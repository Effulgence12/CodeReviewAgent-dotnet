using System.ClientModel;
using CodeReviewAgent.Core.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using OpenAI;

namespace CodeReviewAgent.Core.Llm;

/// <summary>
/// 负责把 Semantic Kernel 接到 OpenAI 兼容端点。通过自定义
/// <see cref="OpenAIClientOptions.Endpoint"/> 把官方 OpenAI SDK 指向任意兼容端点，
/// 从而支持 glm / qwen / deepseek 等模型。
///
/// 每次审查/对话会话创建一个独立 Kernel 并注入该会话所需的工具插件 ——
/// 因为文件系统等工具需要按「审查根目录」做沙箱隔离，无法做成全局单例。
/// </summary>
public sealed class KernelFactory
{
    private readonly AgentConfig _config;
    private readonly ILoggerFactory _loggerFactory;

    public KernelFactory(IOptions<AgentConfig> config, ILoggerFactory loggerFactory)
    {
        _config = config.Value;
        _loggerFactory = loggerFactory;
    }

    /// <summary>暴露 LLM 配置，供前端显示模型名/是否已配置 Key。</summary>
    public LlmOptions Llm => _config.Llm;

    /// <summary>构造一个挂载了指定工具插件的 Kernel。</summary>
    /// <param name="plugins">本会话要挂载的工具插件；为空表示纯对话（如主审汇总）。</param>
    public Kernel CreateKernel(IEnumerable<KernelPlugin>? plugins = null)
    {
        // 缺 Key 直接显式报错 —— 本程序所有推理均由真实大模型驱动，绝不用假数据冒充。
        if (!_config.Llm.IsConfigured)
        {
            throw new InvalidOperationException(
                "未配置 LLM ApiKey。请在运行项目目录下复制 appsettings.Local.json.example 为 appsettings.Local.json 并填入 Llm:ApiKey，" +
                "或设置环境变量 Llm__ApiKey。缺少 Key 无法运行（不会用假数据冒充）。");
        }

        // 用自定义 Endpoint 把官方 OpenAI 客户端指向兼容端点。
        var clientOptions = new OpenAIClientOptions { Endpoint = new Uri(_config.Llm.Endpoint) };
        var openAIClient = new OpenAIClient(new ApiKeyCredential(_config.Llm.ApiKey), clientOptions);

        var builder = Kernel.CreateBuilder();
        builder.Services.AddSingleton(_loggerFactory);
        builder.AddOpenAIChatCompletion(modelId: _config.Llm.ChatModel, openAIClient: openAIClient);

        var kernel = builder.Build();
        if (plugins is not null)
        {
            foreach (var plugin in plugins)
            {
                kernel.Plugins.Add(plugin);
            }
        }
        return kernel;
    }
}
