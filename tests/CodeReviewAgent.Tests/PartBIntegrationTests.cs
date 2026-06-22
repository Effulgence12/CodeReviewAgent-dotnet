using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Rag;
using CodeReviewAgent.Core.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Tests;

public sealed class PartBIntegrationTests
{
    [Fact]
    public void ToolChain_ListReadAnalyzeFixCompile_Completes()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Sample.cs", "public class Sample { public int Value() => 42; }");
        var files = new FileSystemPlugin(temp.Path);
        var analyzer = new RoslynAnalysisPlugin(temp.Path);
        var fixer = new FixPlugin(temp.Path);
        var compiler = new CompileCheckPlugin(temp.Path);

        Assert.Contains("Sample.cs", files.ListFiles());
        Assert.Contains("1: public class Sample", files.ReadFile("Sample.cs"));
        Assert.Contains("CRA003", analyzer.AnalyzeCode("Sample.cs"));

        var fixResult = fixer.ProposeFix(
            "Sample.cs",
            "replace magic number",
            "public class Sample { private const int Answer = 42; public int Value() => Answer; }");

        Assert.Contains("Sample.cs.bak", fixResult);
        Assert.StartsWith("Success", compiler.CompileCheck("Sample.cs"));
    }

    [Fact]
    public async Task KnowledgeChain_DifferentQueriesHitDifferentTopics()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("naming.md", "# Naming\nPascalCase camelCase method names.");
        temp.WriteFile("security.md", "# Security\nPath traversal API Key secret validation.");
        var knowledgeBase = new KnowledgeBase(
            new TopicEmbeddingService(),
            Options.Create(new AgentConfig
            {
                Rag = new RagOptions
                {
                    KnowledgeDirectory = temp.Path,
                    ChunkSize = 1000,
                    ChunkOverlap = 100,
                },
            }),
            NullLogger<KnowledgeBase>.Instance);
        var plugin = new CodingStandardsPlugin(knowledgeBase, topK: 1);

        var naming = await plugin.SearchStandardsAsync("PascalCase naming");
        var security = await plugin.SearchStandardsAsync("path traversal security");

        Assert.Contains("naming.md", naming);
        Assert.Contains("security.md", security);
        Assert.NotEqual(naming, security);
    }

    private sealed class TopicEmbeddingService : IEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            IReadOnlyList<float[]> vectors = texts.Select(text => new[]
            {
                ContainsAny(text, "PascalCase", "camelCase", "naming") ? 1f : 0f,
                ContainsAny(text, "traversal", "security", "API Key") ? 1f : 0f,
            }).ToArray();
            return Task.FromResult(vectors);
        }

        private static bool ContainsAny(string text, params string[] terms) =>
            terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
    }
}
