using CodeReviewAgent.Core.Tools;

namespace CodeReviewAgent.Tests;

public sealed class FixSessionTests
{
    [Fact]
    public void Stage_GeneratesDiffWithoutChangingSourceFile()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Sample.cs", "class Sample { int Value() => 1; }");
        var session = new FixSession(temp.Path);

        var pending = session.Stage(
            "Sample.cs",
            "replace magic number",
            "class Sample { private const int Answer = 42; int Value() => Answer; }");

        Assert.Single(session.PendingFixes);
        Assert.Contains("--- a/Sample.cs", pending.UnifiedDiff);
        Assert.Contains("+class Sample", pending.UnifiedDiff);
        Assert.Contains("=> 1", File.ReadAllText(Path.Combine(temp.Path, "Sample.cs")));
        Assert.False(File.Exists(Path.Combine(temp.Path, "Sample.cs.bak")));
    }

    [Fact]
    public void Apply_CreatesBackupThenRollback_RestoresOriginalContent()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.WriteFile("Sample.cs", "class Sample { int Value() => 1; }");
        var session = new FixSession(temp.Path);
        var pending = session.Stage("Sample.cs", "update value", "class Sample { int Value() => 2; }");

        var applied = session.Apply(pending.Id);

        Assert.Equal("Sample.cs.bak", applied.BackupPath);
        Assert.Contains("=> 2", File.ReadAllText(source));
        Assert.Contains("=> 1", session.ReadBackup("Sample.cs"));
        Assert.Empty(session.PendingFixes);

        session.Rollback("Sample.cs");

        Assert.Contains("=> 1", File.ReadAllText(source));
        Assert.True(File.Exists(source + ".bak"));
    }

    [Fact]
    public void Apply_RejectsStagedFixWhenSourceChangedAfterPreview()
    {
        using var temp = new TemporaryDirectory();
        var source = temp.WriteFile("Sample.cs", "class Sample { int Value() => 1; }");
        var session = new FixSession(temp.Path);
        var pending = session.Stage("Sample.cs", "update value", "class Sample { int Value() => 2; }");
        File.WriteAllText(source, "class Sample { int Value() => 3; }");

        var exception = Assert.Throws<InvalidOperationException>(() => session.Apply(pending.Id));

        Assert.Contains("已变化", exception.Message);
        Assert.Single(session.PendingFixes);
        Assert.False(File.Exists(source + ".bak"));
    }
}
