using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Tests;

public sealed class KnowledgeCorpusTests
{
    [Fact]
    public async Task NamingCorpus_IsPresentAndSearchable()
    {
        var result = await LoadAndSearchAsync(
            "naming.md", "命名 参数 PascalCase", "PascalCase", "camelCase", "Async");

        Assert.Contains("naming.md", result);
    }

    [Fact]
    public async Task ExceptionCorpus_IsPresentAndSearchable()
    {
        var result = await LoadAndSearchAsync(
            "exceptions.md", "异常 资源释放 catch", "catch", "IDisposable", "throw;");

        Assert.Contains("exceptions.md", result);
    }

    [Fact]
    public async Task AsyncCorpus_IsPresentAndSearchable()
    {
        var result = await LoadAndSearchAsync(
            "async.md", "异步 await CancellationToken", "await", "CancellationToken", "async void");

        Assert.Contains("async.md", result);
    }

    [Fact]
    public async Task SecurityCorpus_IsPresentAndSearchable()
    {
        var result = await LoadAndSearchAsync(
            "security.md", "安全 路径穿越 密钥", "路径穿越", "API Key", "最小权限");

        Assert.Contains("security.md", result);
    }

    [Fact]
    public async Task PerformanceCorpus_IsPresentAndSearchable()
    {
        var result = await LoadAndSearchAsync(
            "performance.md", "性能 分配 热路径", "热路径", "分配", "Profiler");

        Assert.Contains("performance.md", result);
    }

    [Fact]
    public async Task SolidCorpus_IsPresentAndSearchable()
    {
        var result = await LoadAndSearchAsync(
            "solid.md", "SOLID 依赖倒置 职责", "SOLID", "依赖倒置", "单一职责");

        Assert.Contains("solid.md", result);
    }

    private static async Task<string> LoadAndSearchAsync(
        string fileName, string query, params string[] requiredTerms)
    {
        var knowledgeDirectory = DirectoryLocator.Resolve("knowledge")
            ?? throw new DirectoryNotFoundException("knowledge directory not found");
        var sourcePath = System.IO.Path.Combine(knowledgeDirectory, fileName);
        Assert.True(File.Exists(sourcePath), $"Missing corpus file: {fileName}");
        var content = File.ReadAllText(sourcePath);
        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.All(requiredTerms, term => Assert.Contains(term, content, StringComparison.OrdinalIgnoreCase));

        using var temp = new TemporaryDirectory();
        temp.WriteFile(fileName, content);
        var knowledgeBase = new KnowledgeBase(
            new KeywordEmbeddingService(requiredTerms),
            Options.Create(new AgentConfig
            {
                Rag = new RagOptions
                {
                    KnowledgeDirectory = temp.Path,
                    ChunkSize = 4000,
                    ChunkOverlap = 100,
                },
            }),
            NullLogger<KnowledgeBase>.Instance);

        return Assert.Single(await knowledgeBase.SearchAsync(query, 1));
    }

    private sealed class KeywordEmbeddingService : IEmbeddingService
    {
        private readonly string[] _terms;

        public KeywordEmbeddingService(string[] terms) => _terms = terms;

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            IReadOnlyList<float[]> vectors = texts.Select(text => _terms
                .Select(term => text.Contains(term, StringComparison.OrdinalIgnoreCase) ? 1f : 0f)
                .ToArray()).ToArray();
            return Task.FromResult(vectors);
        }
    }
}
