using CodeReviewAgent.Core.Agent;
using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Llm;
using CodeReviewAgent.Core.Orchestration;
using CodeReviewAgent.Core.Rag;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CodeReviewAgent.Core;

/// <summary>把代码审查 Agent 的全部核心服务一次性注册进 DI 容器。</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddCodeReviewAgent(this IServiceCollection services, IConfiguration configuration)
    {
        // 强类型配置：既支持 "CodeReviewAgent" 配置节，也支持顶层键（Llm/Embedding/Rag/Agent），
        // 便于 appsettings.Local.json / 环境变量直接注入。
        services.AddOptions<AgentConfig>()
            .Bind(configuration.GetSection("CodeReviewAgent"))
            .Bind(configuration);

        services.AddHttpClient();
        // embedding 专用客户端：缩短超时，让慢网络下卡住的请求快速失败并由重试换新连接重试，
        // 而不是干等默认的 100 秒。配合 EmbeddingService 的分批并行与瞬时错误重试。
        services.AddHttpClient(nameof(EmbeddingService), client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        // 引擎与编排（A 负责）。
        services.AddSingleton<KernelFactory>();
        services.AddSingleton<PluginCatalog>();
        services.AddSingleton<ReviewOrchestrator>();
        services.AddSingleton<ConversationalAgentFactory>();

        // RAG（B 负责实现，这里按契约注册）。
        services.AddSingleton<IEmbeddingService, EmbeddingService>();
        services.AddSingleton<IKnowledgeBase, KnowledgeBase>();

        return services;
    }
}
