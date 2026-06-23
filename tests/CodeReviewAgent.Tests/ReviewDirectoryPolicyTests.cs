using CodeReviewAgent.Core.Configuration;
using Microsoft.Extensions.Options;

namespace CodeReviewAgent.Tests;

public sealed class ReviewDirectoryPolicyTests
{
    [Fact]
    public void ResolveDirectory_WhenArbitraryDirectoriesEnabled_AllowsExistingDirectory()
    {
        using var temp = new TemporaryDirectory();
        var policy = CreatePolicy(allowArbitraryDirectories: true);

        var result = policy.ResolveDirectory(temp.Path);

        Assert.Equal(Path.GetFullPath(temp.Path), result);
    }

    [Fact]
    public void ResolveDirectory_WhenRestricted_AllowsConfiguredSharedRootAndChild()
    {
        using var root = new TemporaryDirectory();
        var child = Directory.CreateDirectory(Path.Combine(root.Path, "shared", "project"));
        var policy = CreatePolicy(sharedRoots: new[] { Path.Combine(root.Path, "shared") });

        var result = policy.ResolveDirectory(child.FullName);

        Assert.Equal(Path.GetFullPath(child.FullName), result);
    }

    [Fact]
    public void ResolveDirectory_WhenRestricted_RejectsDirectoryOutsideSharedRoots()
    {
        using var allowed = new TemporaryDirectory();
        using var outside = new TemporaryDirectory();
        var policy = CreatePolicy(sharedRoots: new[] { allowed.Path });

        var exception = Assert.Throws<UnauthorizedAccessException>(() => policy.ResolveDirectory(outside.Path));

        Assert.Contains("不在允许的审查目录", exception.Message);
    }

    [Fact]
    public void ResolveCSharpFile_RequiresExistingCSharpFileWithinPolicy()
    {
        using var root = new TemporaryDirectory();
        var csharp = root.WriteFile("Code.cs", "class Code { }");
        root.WriteFile("notes.txt", "not source");
        var policy = CreatePolicy(sharedRoots: new[] { root.Path });

        Assert.Equal(Path.GetFullPath(csharp), policy.ResolveCSharpFile(csharp));
        Assert.Throws<ArgumentException>(() => policy.ResolveCSharpFile(Path.Combine(root.Path, "notes.txt")));
    }

    private static ReviewDirectoryPolicy CreatePolicy(
        bool allowArbitraryDirectories = false,
        IEnumerable<string>? sharedRoots = null) =>
        new(Options.Create(new AgentConfig
        {
            ReviewAccess = new ReviewAccessOptions
            {
                AllowArbitraryDirectories = allowArbitraryDirectories,
                SharedRoots = sharedRoots?.ToList() ?? new List<string>(),
            },
        }));
}
