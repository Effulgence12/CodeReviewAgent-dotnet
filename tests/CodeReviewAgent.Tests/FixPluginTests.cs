using CodeReviewAgent.Core.Tools;

namespace CodeReviewAgent.Tests;

public sealed class FixPluginTests
{
    [Fact]
    public void Backup_FirstCall_CopiesOriginalContent()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Good.cs", "original");
        var plugin = new FixPlugin(temp.Path);

        var backup = plugin.EnsureBackup("Good.cs");

        Assert.Equal("original", File.ReadAllText(backup));
    }

    [Fact]
    public void Backup_ExistingBackup_DoesNotOverwrite()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Good.cs", "first");
        var plugin = new FixPlugin(temp.Path);
        var backup = plugin.EnsureBackup("Good.cs");
        File.WriteAllText(System.IO.Path.Combine(temp.Path, "Good.cs"), "second");

        plugin.EnsureBackup("Good.cs");

        Assert.Equal("first", File.ReadAllText(backup));
    }

    [Fact]
    public void Backup_Traversal_ThrowsWithoutCreatingFiles()
    {
        using var temp = new TemporaryDirectory();
        var plugin = new FixPlugin(temp.Path);

        Assert.Throws<UnauthorizedAccessException>(() => plugin.EnsureBackup("../Outside.cs"));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.bak", SearchOption.AllDirectories));
    }

    [Fact]
    public void Backup_DoesNotChangeSource()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.WriteFile("Good.cs", "original");
        var plugin = new FixPlugin(temp.Path);

        plugin.EnsureBackup("Good.cs");

        Assert.Equal("original", File.ReadAllText(source));
    }

    [Fact]
    public void ProposeFix_ValidUpdate_WritesAndKeepsOriginalBackup()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.WriteFile("Good.cs", "original");
        var plugin = new FixPlugin(temp.Path);

        var result = plugin.ProposeFix("Good.cs", "rename", "updated");

        Assert.Equal("updated", File.ReadAllText(source));
        Assert.Equal("original", File.ReadAllText(source + ".bak"));
        Assert.Contains("Good.cs", result);
        Assert.Contains("rename", result);
    }

    [Fact]
    public void ProposeFix_SecondUpdate_PreservesFirstBackup()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.WriteFile("Good.cs", "first");
        var plugin = new FixPlugin(temp.Path);

        plugin.ProposeFix("Good.cs", "one", "second");
        plugin.ProposeFix("Good.cs", "two", "third");

        Assert.Equal("third", File.ReadAllText(source));
        Assert.Equal("first", File.ReadAllText(source + ".bak"));
    }

    [Fact]
    public void ProposeFix_EmptyContent_Throws()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Good.cs", "original");
        var plugin = new FixPlugin(temp.Path);

        Assert.Throws<ArgumentException>(() => plugin.ProposeFix("Good.cs", "issue", "  "));
        Assert.False(File.Exists(System.IO.Path.Combine(temp.Path, "Good.cs.bak")));
    }

    [Fact]
    public void ProposeFix_FailedReplace_PreservesSource()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.WriteFile("Good.cs", "original");
        var plugin = new FixPlugin(temp.Path, (_, _) => throw new IOException("simulated"));

        Assert.Throws<IOException>(() => plugin.ProposeFix("Good.cs", "issue", "updated"));
        Assert.Equal("original", File.ReadAllText(source));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void ProposeFix_ResultDoesNotLeakFullContent()
    {
        using var temp = new TemporaryDirectory();
        const string secretContent = "class Good { string Value = \"unique-full-content\"; }";
        temp.WriteFile("Good.cs", "original");
        var plugin = new FixPlugin(temp.Path);

        var result = plugin.ProposeFix("Good.cs", "safe issue", secretContent);

        Assert.DoesNotContain(secretContent, result);
        Assert.DoesNotContain(temp.Path, result);
    }
}
