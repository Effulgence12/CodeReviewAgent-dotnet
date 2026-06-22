using CodeReviewAgent.Core.Tools;

namespace CodeReviewAgent.Tests;

public sealed class CompileCheckPluginTests
{
    [Fact]
    public void CompileCheck_ValidFrameworkCode_ReturnsSuccess()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Good.cs", "using System.Linq; using System.Collections.Generic; public class Good { public int Sum(IEnumerable<int> values) => values.Sum(); }");
        var plugin = new CompileCheckPlugin(temp.Path);

        var result = plugin.CompileCheck("Good.cs");

        Assert.StartsWith("Success", result);
    }

    [Fact]
    public void CompileCheck_TypeError_ReturnsFailedWithDiagnostic()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Broken.cs", "class Broken { int Value = \"text\"; }");
        var plugin = new CompileCheckPlugin(temp.Path);

        var result = plugin.CompileCheck("Broken.cs");

        Assert.StartsWith("Failed", result);
        Assert.Contains("CS0029", result);
        Assert.Matches(@"\d+:\d+", result);
    }

    [Fact]
    public void CompileCheck_DoesNotWriteAssembly()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Good.cs", "class Good { }");
        var plugin = new CompileCheckPlugin(temp.Path);

        plugin.CompileCheck("Good.cs");

        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.dll", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(temp.Path, "*.pdb", SearchOption.AllDirectories));
    }

    [Fact]
    public void CompileCheck_Traversal_Throws()
    {
        using var temp = new TemporaryDirectory();
        var plugin = new CompileCheckPlugin(temp.Path);

        Assert.Throws<UnauthorizedAccessException>(() => plugin.CompileCheck("../Outside.cs"));
    }
}
