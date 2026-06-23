using CodeReviewAgent.Core.Configuration;
using CodeReviewAgent.Mcp;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Tests;

public sealed class McpReviewToolsTests
{
    [Fact]
    public void AnalyzeCSharp_UsesSharedDirectoryPolicyBeforeReadingFile()
    {
        using var allowed = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var allowedFile = allowed.WriteFile("Sample.cs", "class Sample { void Run() { } }");
        var outsideFile = outside.WriteFile("Outside.cs", "class Outside { }");
        var policy = CreateRestrictedPolicy(allowed.Path);

        var success = McpReviewTools.AnalyzeCSharp(policy, allowedFile);
        var denied = McpReviewTools.AnalyzeCSharp(policy, outsideFile);

        Assert.DoesNotContain("无法分析文件", success);
        Assert.Contains("无法分析文件", denied);
        Assert.Contains("不在允许的审查目录", denied);
    }

    [Fact]
    public void AnalyzeCSharp_RejectsNonCSharpFile()
    {
        using var allowed = new TemporaryDirectory();
        var textFile = allowed.WriteFile("notes.txt", "hello");
        var policy = CreateRestrictedPolicy(allowed.Path);

        var result = McpReviewTools.AnalyzeCSharp(policy, textFile);

        Assert.Contains("只允许分析 .cs 文件", result);
    }

    private static ReviewDirectoryPolicy CreateRestrictedPolicy(string root) => new(
        Options.Create(new AgentConfig
        {
            ReviewAccess = new ReviewAccessOptions
            {
                SharedRoots = new List<string> { root },
            },
        }));
}
