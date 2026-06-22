using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Rag;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Tests;

public sealed class KnowledgeBaseTests
{
    [Fact]
    public void Discover_LoadsMarkdownRecursivelyInStableOrder()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("z.md", "# Z");
        temp.WriteFile("guide/A.md", "# A");
        temp.WriteFile("ignore.txt", "ignore");
        var knowledgeBase = CreateKnowledgeBase(temp.Path);

        var documents = knowledgeBase.DiscoverDocuments();

        Assert.Equal(new[] { "guide/A.md", "z.md" }, documents.Select(document => document.Source));
    }

    [Fact]
    public void Discover_IgnoresEmptyHiddenAndNonMarkdownFiles()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("visible.md", "content");
        temp.WriteFile("empty.md", "  \n");
        temp.WriteFile(".hidden/secret.md", "secret");
        temp.WriteFile("notes.txt", "notes");
        var knowledgeBase = CreateKnowledgeBase(temp.Path);

        var documents = knowledgeBase.DiscoverDocuments();

        Assert.Single(documents);
        Assert.Equal("visible.md", documents[0].Source);
    }

    [Fact]
    public void Discover_MissingDirectory_ThrowsClearError()
    {
        using var temp = new TemporaryDirectory();
        var missing = System.IO.Path.Combine(temp.Path, "missing");
        var knowledgeBase = CreateKnowledgeBase(missing);

        var exception = Assert.Throws<DirectoryNotFoundException>(() => knowledgeBase.DiscoverDocuments());
        Assert.Contains("知识库目录", exception.Message);
    }

    [Fact]
    public void Chunk_SmallDocument_RemainsSingleChunk()
    {
        using var temp = new TemporaryDirectory();
        var knowledgeBase = CreateKnowledgeBase(temp.Path, chunkSize: 200, overlap: 20);
        var documents = new[] { new KnowledgeBase.KnowledgeDocument("one.md", "# Heading\nSmall paragraph.") };

        var chunks = knowledgeBase.ChunkDocuments(documents);

        var chunk = Assert.Single(chunks);
        Assert.Equal("one.md", chunk.Source);
        Assert.Equal("Heading", chunk.Heading);
    }

    [Fact]
    public void Chunk_Markdown_PrefersHeadingAndParagraphBoundaries()
    {
        using var temp = new TemporaryDirectory();
        var knowledgeBase = CreateKnowledgeBase(temp.Path, chunkSize: 200, overlap: 20);
        var document = new KnowledgeBase.KnowledgeDocument(
            "guide.md", "# First\nFirst paragraph.\n\n## Second\nSecond paragraph.");

        var chunks = knowledgeBase.ChunkDocuments(new[] { document });

        Assert.Equal(2, chunks.Count);
        Assert.Equal(new[] { "First", "Second" }, chunks.Select(chunk => chunk.Heading));
    }

    [Fact]
    public void Chunk_LongParagraph_UsesConfiguredOverlap()
    {
        using var temp = new TemporaryDirectory();
        var knowledgeBase = CreateKnowledgeBase(temp.Path, chunkSize: 10, overlap: 2);
        var document = new KnowledgeBase.KnowledgeDocument("long.md", new string('a', 25));

        var chunks = knowledgeBase.ChunkDocuments(new[] { document });

        Assert.True(chunks.Count >= 3);
        Assert.All(chunks, chunk => Assert.InRange(chunk.Content.Length, 1, 10));
        Assert.Equal(chunks[0].Content[^2..], chunks[1].Content[..2]);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, -1)]
    [InlineData(10, 10)]
    public void Chunk_InvalidOptions_Throws(int chunkSize, int overlap)
    {
        using var temp = new TemporaryDirectory();
        var knowledgeBase = CreateKnowledgeBase(temp.Path, chunkSize, overlap);
        var documents = new[] { new KnowledgeBase.KnowledgeDocument("one.md", "text") };

        Assert.Throws<InvalidOperationException>(() => knowledgeBase.ChunkDocuments(documents));
    }

    [Fact]
    public async Task Initialize_EmbedsEveryChunkOnce()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("guide.md", "# One\nFirst.\n\n## Two\nSecond.");
        var embedding = SuccessfulEmbedding();
        var knowledgeBase = CreateKnowledgeBase(temp.Path, embedding: embedding);

        await knowledgeBase.InitializeAsync();

        Assert.Equal(1, embedding.CallCount);
        Assert.Equal(2, embedding.LastTexts!.Count);
    }

    [Fact]
    public async Task Initialize_RepeatedCalls_AreIdempotent()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("guide.md", "content");
        var embedding = SuccessfulEmbedding();
        var knowledgeBase = CreateKnowledgeBase(temp.Path, embedding: embedding);

        await knowledgeBase.InitializeAsync();
        await knowledgeBase.InitializeAsync();

        Assert.Equal(1, embedding.CallCount);
    }

    [Fact]
    public async Task Initialize_ConcurrentCalls_BuildOneIndex()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("guide.md", "content");
        var embedding = new ScriptedEmbeddingService(async (_, texts, ct) =>
        {
            await Task.Delay(30, ct);
            return Vectors(texts.Count);
        });
        var knowledgeBase = CreateKnowledgeBase(temp.Path, embedding: embedding);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => knowledgeBase.InitializeAsync()));

        Assert.Equal(1, embedding.CallCount);
    }

    [Fact]
    public async Task Initialize_Failure_AllowsRetry()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("guide.md", "content");
        var embedding = new ScriptedEmbeddingService((call, texts, _) =>
            call == 1
                ? Task.FromException<IReadOnlyList<float[]>>(new HttpRequestException("first failure"))
                : Task.FromResult(Vectors(texts.Count)));
        var knowledgeBase = CreateKnowledgeBase(temp.Path, embedding: embedding);

        await Assert.ThrowsAsync<HttpRequestException>(() => knowledgeBase.InitializeAsync());
        await knowledgeBase.InitializeAsync();

        Assert.Equal(2, embedding.CallCount);
    }

    [Fact]
    public async Task Initialize_Cancellation_AllowsRetry()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("guide.md", "content");
        var embedding = new ScriptedEmbeddingService(async (call, texts, ct) =>
        {
            if (call == 1)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }
            return Vectors(texts.Count);
        });
        var knowledgeBase = CreateKnowledgeBase(temp.Path, embedding: embedding);
        using var cts = new CancellationTokenSource(20);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => knowledgeBase.InitializeAsync(cts.Token));
        await knowledgeBase.InitializeAsync();

        Assert.Equal(2, embedding.CallCount);
    }

    [Fact]
    public async Task Search_Uninitialized_AutoInitializes()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("one.md", "content");
        var embedding = new ScriptedEmbeddingService((_, texts, _) =>
            Task.FromResult(Vectors(texts.Count)));
        var knowledgeBase = CreateKnowledgeBase(temp.Path, embedding: embedding);

        var results = await knowledgeBase.SearchAsync("query", 1);

        Assert.Single(results);
        Assert.Equal(2, embedding.CallCount);
    }

    [Fact]
    public async Task Search_ReturnsCosineDescendingTopK()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("a.md", "alpha");
        temp.WriteFile("b.md", "beta");
        var embedding = new ScriptedEmbeddingService((call, _, _) => Task.FromResult<IReadOnlyList<float[]>>(
            call == 1 ? new[] { new[] { 1f, 0f }, new[] { 0f, 1f } } : new[] { new[] { 1f, 0f } }));
        var knowledgeBase = CreateKnowledgeBase(temp.Path, embedding: embedding);

        var results = await knowledgeBase.SearchAsync("alpha query", 1);

        var result = Assert.Single(results);
        Assert.Contains("a.md", result);
    }

    [Fact]
    public async Task Search_TopKAboveCount_ReturnsAllOnce()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("a.md", "alpha");
        temp.WriteFile("b.md", "beta");
        var embedding = new ScriptedEmbeddingService((_, texts, _) =>
            Task.FromResult(Vectors(texts.Count)));
        var results = await CreateKnowledgeBase(temp.Path, embedding: embedding).SearchAsync("query", 10);

        Assert.Equal(2, results.Count);
        Assert.Equal(2, results.Distinct().Count());
    }

    [Fact]
    public async Task Search_TiedScores_AreStable()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("b.md", "beta");
        temp.WriteFile("a.md", "alpha");
        var embedding = new ScriptedEmbeddingService((_, texts, _) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                Enumerable.Range(0, texts.Count).Select(_ => new[] { 1f, 0f }).ToArray()));
        var knowledgeBase = CreateKnowledgeBase(temp.Path, embedding: embedding);

        var first = await knowledgeBase.SearchAsync("query", 2);
        var second = await knowledgeBase.SearchAsync("query", 2);

        Assert.Equal(first, second);
        Assert.Contains("a.md", first[0]);
    }

    [Fact]
    public async Task Search_ZeroVector_DoesNotProduceNaN()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("a.md", "alpha");
        var embedding = new ScriptedEmbeddingService((_, texts, _) =>
            Task.FromResult<IReadOnlyList<float[]>>(
                Enumerable.Range(0, texts.Count).Select(_ => new[] { 0f, 0f }).ToArray()));

        var results = await CreateKnowledgeBase(temp.Path, embedding: embedding).SearchAsync("query", 1);

        Assert.Single(results);
        Assert.DoesNotContain("NaN", results[0]);
    }

    [Theory]
    [InlineData("", 1)]
    [InlineData("query", 0)]
    [InlineData("query", -1)]
    public async Task Search_InvalidArguments_Throws(string query, int topK)
    {
        using var temp = new TemporaryDirectory();
        var knowledgeBase = CreateKnowledgeBase(temp.Path);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => knowledgeBase.SearchAsync(query, topK));
    }

    private static KnowledgeBase CreateKnowledgeBase(
        string directory,
        int chunkSize = 600,
        int overlap = 80,
        IEmbeddingService? embedding = null) =>
        new(
            embedding ?? new NeverCalledEmbeddingService(),
            Options.Create(new AgentConfig
            {
                Rag = new RagOptions
                {
                    KnowledgeDirectory = directory,
                    ChunkSize = chunkSize,
                    ChunkOverlap = overlap,
                },
            }),
            NullLogger<KnowledgeBase>.Instance);

    private static ScriptedEmbeddingService SuccessfulEmbedding() =>
        new((_, texts, _) => Task.FromResult(Vectors(texts.Count)));

    private static IReadOnlyList<float[]> Vectors(int count) =>
        Enumerable.Range(0, count).Select(index => new[] { 1f, index + 1f }).ToArray();

    private sealed class NeverCalledEmbeddingService : IEmbeddingService
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default) =>
            throw new InvalidOperationException("Embedding should not be called by discovery tests.");
    }

    private sealed class ScriptedEmbeddingService : IEmbeddingService
    {
        private readonly Func<int, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<float[]>>> _handler;
        private int _callCount;

        public ScriptedEmbeddingService(
            Func<int, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<float[]>>> handler)
        {
            _handler = handler;
        }

        public int CallCount => _callCount;
        public IReadOnlyList<string>? LastTexts { get; private set; }

        public Task<IReadOnlyList<float[]>> EmbedAsync(
            IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            LastTexts = texts;
            var call = Interlocked.Increment(ref _callCount);
            return _handler(call, texts, ct);
        }
    }
}
