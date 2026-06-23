using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Core.Orchestration;
using CodeReviewAgent.Core.Rag;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Tests;

public sealed class ConversationToolPermissionTests
{
    [Fact]
    public void BuildConversationTools_ReadOnlyMode_DoesNotExposeFixOrCompileTools()
    {
        using var temp = new TemporaryDirectory();
        var catalog = CreateCatalog();

        var plugins = catalog.BuildConversationTools(temp.Path, includeWriteTools: false);
        var names = plugins.Select(plugin => plugin.Name).ToArray();

        Assert.Contains("files", names);
        Assert.Contains("roslyn", names);
        Assert.Contains("standards", names);
        Assert.DoesNotContain("fix", names);
        Assert.DoesNotContain("compile", names);
    }

    [Fact]
    public void BuildConversationTools_WriteMode_ExposesFixAndCompileTools()
    {
        using var temp = new TemporaryDirectory();
        var catalog = CreateCatalog();

        var names = catalog.BuildConversationTools(temp.Path, includeWriteTools: true)
            .Select(plugin => plugin.Name)
            .ToArray();

        Assert.Contains("fix", names);
        Assert.Contains("compile", names);
    }

    private static PluginCatalog CreateCatalog() => new(
        new EmptyKnowledgeBase(),
        Options.Create(new AgentConfig()));

    private sealed class EmptyKnowledgeBase : IKnowledgeBase
    {
        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task<IReadOnlyList<string>> SearchAsync(string query, int topK, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
