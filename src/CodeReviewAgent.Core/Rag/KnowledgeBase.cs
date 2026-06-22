using CodeReviewAgent.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Core.Rag;

/// <summary>
/// 编码规范知识库实现（组员 B 负责）。
///
/// 预期技术路线：
///   InitializeAsync —— 用 <see cref="DirectoryLocator"/> 定位 knowledge/ 目录，读取 .md 语料，
///     按 <see cref="RagOptions.ChunkSize"/>/<see cref="RagOptions.ChunkOverlap"/> 分块，
///     调 <see cref="IEmbeddingService"/> 批量向量化，存入内存向量列表（幂等，只初始化一次）。
///   SearchAsync —— 把 query 向量化，与各分块做余弦相似度，返回 topK 个最相关片段。
/// 规模小，内存向量 + 余弦相似度即可，无需外部向量库（如需加分可接 Qdrant）。
/// </summary>
public sealed class KnowledgeBase : IKnowledgeBase
{
    private readonly IEmbeddingService _embedding;
    private readonly RagOptions _rag;
    private readonly ILogger<KnowledgeBase> _logger;
    private readonly SemaphoreSlim _initializeLock = new(1, 1);
    private volatile IReadOnlyList<IndexedChunk>? _index;

    public KnowledgeBase(IEmbeddingService embedding, IOptions<AgentConfig> config, ILogger<KnowledgeBase> logger)
    {
        _embedding = embedding;
        _rag = config.Value.Rag;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_index is not null)
        {
            return;
        }

        await _initializeLock.WaitAsync(ct);
        try
        {
            if (_index is not null)
            {
                return;
            }

            var chunks = ChunkDocuments(DiscoverDocuments());
            if (chunks.Count == 0)
            {
                throw new InvalidOperationException("知识库没有可索引的 Markdown 内容。 ");
            }

            var vectors = await _embedding.EmbedAsync(
                chunks.Select(chunk => chunk.Content).ToArray(), ct);
            if (vectors.Count != chunks.Count)
            {
                throw new InvalidDataException("知识块数量与向量数量不一致。 ");
            }
            if (vectors.Any(vector => vector is null || vector.Length == 0) ||
                vectors.Select(vector => vector.Length).Distinct().Count() != 1)
            {
                throw new InvalidDataException("知识库向量为空或维度不一致。 ");
            }

            var completed = chunks.Zip(vectors, (chunk, vector) => new IndexedChunk(chunk, vector)).ToArray();
            _index = completed;
            _logger.LogInformation("知识库初始化完成：{Documents} 个文档，{Chunks} 个分块。",
                chunks.Select(chunk => chunk.Source).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                chunks.Count);
        }
        finally
        {
            _initializeLock.Release();
        }
    }

    public async Task<IReadOnlyList<string>> SearchAsync(string query, int topK, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("检索查询不能为空。", nameof(query));
        }
        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "topK 必须大于 0。 ");
        }

        await InitializeAsync(ct);
        var queryVectors = await _embedding.EmbedAsync(new[] { query }, ct);
        if (queryVectors.Count != 1 || queryVectors[0] is null || queryVectors[0].Length == 0)
        {
            throw new InvalidDataException("查询向量响应无效。 ");
        }

        var index = _index ?? throw new InvalidOperationException("知识库尚未完成初始化。 ");
        if (queryVectors[0].Length != index[0].Vector.Length)
        {
            throw new InvalidDataException("查询向量与知识库向量维度不一致。 ");
        }

        return index.Select(item => new
            {
                item.Chunk,
                Score = CosineSimilarity(queryVectors[0], item.Vector),
            })
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Chunk.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Chunk.Source, StringComparer.Ordinal)
            .ThenBy(item => item.Chunk.Heading, StringComparer.Ordinal)
            .ThenBy(item => item.Chunk.Content, StringComparer.Ordinal)
            .Take(Math.Min(topK, index.Count))
            .Select(item => item.Chunk.DisplayText)
            .ToArray();
    }

    internal IReadOnlyList<KnowledgeDocument> DiscoverDocuments()
    {
        var directory = DirectoryLocator.Resolve(_rag.KnowledgeDirectory);
        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"找不到知识库目录：{_rag.KnowledgeDirectory}");
        }

        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = false,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive,
        };
        return Directory.EnumerateFiles(directory, "*.md", options)
            .Where(path => !HasHiddenPathSegment(directory, path))
            .Select(path => new KnowledgeDocument(
                Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(path)))
            .Where(document => !string.IsNullOrWhiteSpace(document.Content))
            .OrderBy(document => document.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(document => document.Source, StringComparer.Ordinal)
            .ToArray();
    }

    internal IReadOnlyList<KnowledgeChunk> ChunkDocuments(IReadOnlyList<KnowledgeDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ValidateChunkOptions();
        var chunks = new List<KnowledgeChunk>();
        foreach (var document in documents)
        {
            foreach (var block in SplitMarkdownBlocks(document.Content))
            {
                if (block.Text.Length <= _rag.ChunkSize)
                {
                    chunks.Add(new KnowledgeChunk(document.Source, block.Heading, block.Text));
                    continue;
                }

                var step = _rag.ChunkSize - _rag.ChunkOverlap;
                for (var start = 0; start < block.Text.Length; start += step)
                {
                    var length = Math.Min(_rag.ChunkSize, block.Text.Length - start);
                    chunks.Add(new KnowledgeChunk(
                        document.Source,
                        block.Heading,
                        block.Text.Substring(start, length)));
                    if (start + length >= block.Text.Length)
                    {
                        break;
                    }
                }
            }
        }
        return chunks.Where(chunk => !string.IsNullOrWhiteSpace(chunk.Content)).ToArray();
    }

    private void ValidateChunkOptions()
    {
        if (_rag.ChunkSize <= 0)
        {
            throw new InvalidOperationException("Rag.ChunkSize 必须大于 0。 ");
        }
        if (_rag.ChunkOverlap < 0 || _rag.ChunkOverlap >= _rag.ChunkSize)
        {
            throw new InvalidOperationException("Rag.ChunkOverlap 必须大于等于 0 且小于 ChunkSize。 ");
        }
    }

    private static IReadOnlyList<MarkdownBlock> SplitMarkdownBlocks(string content)
    {
        var normalized = content.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var blocks = new List<MarkdownBlock>();
        var paragraph = new List<string>();
        var heading = string.Empty;

        void Flush()
        {
            var text = string.Join("\n", paragraph).Trim();
            if (text.Length > 0)
            {
                blocks.Add(new MarkdownBlock(heading, text));
            }
            paragraph.Clear();
        }

        foreach (var line in normalized.Split('\n'))
        {
            if (line.TrimStart().StartsWith("#", StringComparison.Ordinal))
            {
                Flush();
                heading = line.Trim().TrimStart('#').Trim();
                paragraph.Add(line.TrimEnd());
            }
            else if (string.IsNullOrWhiteSpace(line))
            {
                Flush();
            }
            else
            {
                paragraph.Add(line.TrimEnd());
            }
        }
        Flush();
        return blocks;
    }

    private static bool HasHiddenPathSegment(string root, string path) =>
        Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment.StartsWith(".", StringComparison.Ordinal));

    private static double CosineSimilarity(float[] left, float[] right)
    {
        double dot = 0;
        double leftNorm = 0;
        double rightNorm = 0;
        for (var index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        if (leftNorm == 0 || rightNorm == 0)
        {
            return 0;
        }
        return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
    }

    internal sealed record KnowledgeDocument(string Source, string Content);
    internal sealed record KnowledgeChunk(string Source, string Heading, string Content)
    {
        public string DisplayText => string.IsNullOrWhiteSpace(Heading)
            ? $"来源：{Source}{Environment.NewLine}{Content}"
            : $"来源：{Source}#{Heading}{Environment.NewLine}{Content}";
    }
    private sealed record MarkdownBlock(string Heading, string Text);
    private sealed record IndexedChunk(KnowledgeChunk Chunk, float[] Vector);
}
