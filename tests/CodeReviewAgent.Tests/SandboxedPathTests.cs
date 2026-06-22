using CodeReviewAgent.Core.Tools;

namespace CodeReviewAgent.Tests;

public sealed class SandboxedPathTests
{
    [Fact]
    public void Resolve_ValidRelativeCsFile_ReturnsFullPath()
    {
        using var temp = new TemporaryDirectory();
        var expected = temp.WriteFile("src/Good.cs", "class Good { }");
        var resolver = new SandboxedPathResolver(temp.Path);

        var actual = resolver.ResolveExistingCSharpFile("src/Good.cs");

        Assert.Equal(
            System.IO.Path.GetFullPath(expected),
            System.IO.Path.GetFullPath(actual),
            ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public void Resolve_Traversal_Throws()
    {
        using var temp = new TemporaryDirectory();
        var resolver = new SandboxedPathResolver(temp.Path);

        Assert.Throws<UnauthorizedAccessException>(() =>
            resolver.ResolveExistingCSharpFile("../outside.cs"));
    }

    [Fact]
    public void Resolve_AbsolutePath_Throws()
    {
        using var temp = new TemporaryDirectory();
        var file = temp.WriteFile("Good.cs", "class Good { }");
        var resolver = new SandboxedPathResolver(temp.Path);

        Assert.Throws<ArgumentException>(() => resolver.ResolveExistingCSharpFile(file));
    }

    [Fact]
    public void Resolve_SiblingWithSamePrefix_Throws()
    {
        using var parent = new TemporaryDirectory();
        var root = Directory.CreateDirectory(System.IO.Path.Combine(parent.Path, "review")).FullName;
        var sibling = Directory.CreateDirectory(System.IO.Path.Combine(parent.Path, "review-copy")).FullName;
        File.WriteAllText(System.IO.Path.Combine(sibling, "Outside.cs"), "class Outside { }");
        var resolver = new SandboxedPathResolver(root);

        Assert.Throws<UnauthorizedAccessException>(() =>
            resolver.ResolveExistingCSharpFile("../review-copy/Outside.cs"));
    }

    [Fact]
    public void Resolve_NonCsFile_Throws()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("notes.txt", "not source");
        var resolver = new SandboxedPathResolver(temp.Path);

        Assert.Throws<ArgumentException>(() => resolver.ResolveExistingCSharpFile("notes.txt"));
    }
}
