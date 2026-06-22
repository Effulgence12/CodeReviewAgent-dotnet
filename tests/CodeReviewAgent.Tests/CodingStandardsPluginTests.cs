using CodeReviewAgent.Core.Rag;
using CodeReviewAgent.Core.Tools;

namespace CodeReviewAgent.Tests;

public sealed class CodingStandardsPluginTests
{
    [Fact]
    public async Task SearchStandards_InitializesThenSearches()
    {
        var knowledge = new RecordingKnowledgeBase(new[] { "hit" });
        var plugin = new CodingStandardsPlugin(knowledge);

        await plugin.SearchStandardsAsync("query");

        Assert.Equal(new[] { "initialize", "search" }, knowledge.Calls);
    }

    [Fact]
    public async Task SearchStandards_ForwardsQueryTopKAndCancellation()
    {
        var knowledge = new RecordingKnowledgeBase(new[] { "hit" });
        var plugin = new CodingStandardsPlugin(knowledge, topK: 3);
        using var cts = new CancellationTokenSource();

        await plugin.SearchStandardsAsync("async rules", cts.Token);

        Assert.Equal("async rules", knowledge.Query);
        Assert.Equal(3, knowledge.TopK);
        Assert.Equal(cts.Token, knowledge.CancellationToken);
    }

    [Fact]
    public async Task SearchStandards_NoHits_ReturnsClearMessage()
    {
        var plugin = new CodingStandardsPlugin(new RecordingKnowledgeBase(Array.Empty<string>()));

        var result = await plugin.SearchStandardsAsync("query");

        Assert.Equal("未检索到相关规范条款。", result);
    }

    [Fact]
    public async Task SearchStandards_Hits_AreSeparated()
    {
        var plugin = new CodingStandardsPlugin(new RecordingKnowledgeBase(new[] { "one", "two" }));

        var result = await plugin.SearchStandardsAsync("query");

        Assert.Equal("one\n\n---\n\ntwo", result);
    }

    private sealed class RecordingKnowledgeBase : IKnowledgeBase
    {
        private readonly IReadOnlyList<string> _hits;

        public RecordingKnowledgeBase(IReadOnlyList<string> hits) => _hits = hits;

        public List<string> Calls { get; } = new();
        public string? Query { get; private set; }
        public int TopK { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default)
        {
            Calls.Add("initialize");
            CancellationToken = ct;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> SearchAsync(
            string query, int topK, CancellationToken ct = default)
        {
            Calls.Add("search");
            Query = query;
            TopK = topK;
            CancellationToken = ct;
            return Task.FromResult(_hits);
        }
    }
}
