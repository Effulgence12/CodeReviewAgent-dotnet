using CodeReviewAgent.Core.Tools;

namespace CodeReviewAgent.Tests;

public sealed class RoslynAnalysisPluginTests
{
    [Fact]
    public void Analyze_ValidCode_HasNoSyntaxFinding()
    {
        var result = RoslynAnalysisPlugin.Analyze("public sealed class Good { }");
        Assert.DoesNotContain("CRA000", result);
    }

    [Fact]
    public void Analyze_InvalidCode_ReturnsCra000WithLocation()
    {
        var result = RoslynAnalysisPlugin.Analyze("class Broken\n{\n    void M(\n}");
        Assert.Contains("CRA000", result);
        Assert.Matches(@"CRA000 \| Error \| \d+:\d+", result);
    }

    [Fact]
    public void Analyze_MultipleDiagnostics_AreStable()
    {
        const string code = "class Broken { void M( { int x = ; }";
        var first = RoslynAnalysisPlugin.Analyze(code);
        var second = RoslynAnalysisPlugin.Analyze(code);

        Assert.Equal(first, second);
        Assert.True(first.Split("CRA000").Length > 2);
    }

    [Fact]
    public void Analyze_EmptyCatch_ReturnsCra001()
    {
        var result = RoslynAnalysisPlugin.Analyze("class C { void M() { try { } catch { } } }");
        Assert.Contains("CRA001", result);
    }

    [Fact]
    public void Analyze_CommentOnlyCatch_ReturnsCra001()
    {
        var result = RoslynAnalysisPlugin.Analyze("class C { void M() { try { } catch { /* ignored */ } } }");
        Assert.Contains("CRA001", result);
    }

    [Fact]
    public void Analyze_NonEmptyCatch_DoesNotReturnCra001()
    {
        var result = RoslynAnalysisPlugin.Analyze("class C { void M() { try { } catch { throw; } } }");
        Assert.DoesNotContain("CRA001", result);
    }

    [Fact]
    public void Analyze_MethodOverThreshold_ReturnsCra002()
    {
        Assert.Contains("CRA002", RoslynAnalysisPlugin.Analyze(CreateMethodWithSpan(51)));
    }

    [Fact]
    public void Analyze_MethodAtThreshold_DoesNotReturnCra002()
    {
        Assert.DoesNotContain("CRA002", RoslynAnalysisPlugin.Analyze(CreateMethodWithSpan(50)));
    }

    [Fact]
    public void Analyze_LongConstructor_ReturnsCra002()
    {
        var lines = Enumerable.Repeat("        // filler", 48);
        var code = "class C\n{\n    C()\n    {\n" + string.Join("\n", lines) + "\n    }\n}";

        Assert.Contains("CRA002", RoslynAnalysisPlugin.Analyze(code));
    }

    [Fact]
    public void Analyze_LongLocalFunction_ReturnsCra002()
    {
        var lines = Enumerable.Repeat("            // filler", 48);
        var code = "class C\n{\n    void Outer()\n    {\n        void Local()\n        {\n" +
                   string.Join("\n", lines) + "\n        }\n    }\n}";

        Assert.Contains("CRA002", RoslynAnalysisPlugin.Analyze(code));
    }

    [Fact]
    public void Analyze_MagicNumber_ReturnsCra003()
    {
        var result = RoslynAnalysisPlugin.Analyze("class C { int M() => 42; }");
        Assert.Contains("CRA003", result);
    }

    [Fact]
    public void Analyze_AllowedNumbers_DoNotReturnCra003()
    {
        var result = RoslynAnalysisPlugin.Analyze("class C { int M(int x) => x + -1 + 0 + 1 + 2; }");
        Assert.DoesNotContain("CRA003", result);
    }

    [Fact]
    public void Analyze_ConstAndEnum_DoNotReturnCra003()
    {
        var code = "enum E { Value = 42 } class C { const int Limit = 99; int M() { const int local = 7; return local; } }";
        Assert.DoesNotContain("CRA003", RoslynAnalysisPlugin.Analyze(code));
    }

    [Fact]
    public void Analyze_AsyncWithoutAwait_ReturnsCra004()
    {
        var result = RoslynAnalysisPlugin.Analyze("using System.Threading.Tasks; class C { async Task M() { } }");
        Assert.Contains("CRA004", result);
    }

    [Fact]
    public void Analyze_AsyncWithAwait_DoesNotReturnCra004()
    {
        var result = RoslynAnalysisPlugin.Analyze("using System.Threading.Tasks; class C { async Task M() { await Task.Delay(1); } }");
        Assert.DoesNotContain("CRA004", result);
    }

    [Fact]
    public void Analyze_AwaitOnlyInNestedLambda_StillReturnsCra004()
    {
        var code = "using System; using System.Threading.Tasks; class C { async Task M() { Func<Task> f = async () => await Task.Delay(1); } }";
        Assert.Contains("CRA004", RoslynAnalysisPlugin.Analyze(code));
    }

    [Fact]
    public void Analyze_InvalidMemberNames_ReturnCra005()
    {
        var code = "class bad_type { int bad_property { get; } void bad_method() { } }";
        var result = RoslynAnalysisPlugin.Analyze(code);

        Assert.True(result.Split("CRA005").Length >= 4);
    }

    [Fact]
    public void Analyze_InvalidLocalNames_ReturnCra005()
    {
        var code = "class Good { void Method(int BadParameter) { int BadLocal = 0; } }";
        var result = RoslynAnalysisPlugin.Analyze(code);

        Assert.True(result.Split("CRA005").Length >= 3);
    }

    [Fact]
    public void Analyze_ValidNames_DoNotReturnCra005()
    {
        var code = "class GoodType { int GoodProperty { get; } void GoodMethod(int goodParameter) { int goodLocal = 0; } }";
        Assert.DoesNotContain("CRA005", RoslynAnalysisPlugin.Analyze(code));
    }

    [Fact]
    public void Analyze_Discard_DoesNotReturnCra005()
    {
        var code = "class Good { void Method() { int _ = 0; } }";
        Assert.DoesNotContain("CRA005", RoslynAnalysisPlugin.Analyze(code));
    }

    [Fact]
    public void Analyze_CommentMarkers_ReturnCra006()
    {
        var code = "class Good { // TODO first\n/* FIXME second */ void Method() { } // HACK third\n}";
        var result = RoslynAnalysisPlugin.Analyze(code);

        Assert.Equal(3, result.Split("CRA006").Length - 1);
    }

    [Fact]
    public void Analyze_MarkersAreCaseInsensitive()
    {
        Assert.Contains("CRA006", RoslynAnalysisPlugin.Analyze("class Good { // todo later\n}"));
    }

    [Fact]
    public void Analyze_MarkerInString_DoesNotReturnCra006()
    {
        Assert.DoesNotContain("CRA006", RoslynAnalysisPlugin.Analyze("class Good { string Text = \"TODO is text\"; }"));
    }

    [Fact]
    public void AnalyzeCode_ValidFile_ReturnsFindingsWithRelativePath()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("src/Sample.cs", "class Good { int Method() => 42; }");
        var plugin = new RoslynAnalysisPlugin(temp.Path);

        var result = plugin.AnalyzeCode("src/Sample.cs");

        Assert.Contains("文件: src/Sample.cs", result);
        Assert.Contains("CRA003", result);
        Assert.DoesNotContain(temp.Path, result);
    }

    [Fact]
    public void AnalyzeCode_Traversal_Throws()
    {
        using var temp = new TemporaryDirectory();
        var plugin = new RoslynAnalysisPlugin(temp.Path);

        Assert.Throws<UnauthorizedAccessException>(() => plugin.AnalyzeCode("../Outside.cs"));
    }

    [Fact]
    public void AnalyzeCode_MatchesPureAnalyzerRules()
    {
        using var temp = new TemporaryDirectory();
        const string code = "class Good { void Method() { try { } catch { } } }";
        temp.WriteFile("Good.cs", code);
        var plugin = new RoslynAnalysisPlugin(temp.Path);

        var direct = RoslynAnalysisPlugin.Analyze(code);
        var fromFile = plugin.AnalyzeCode("Good.cs");

        Assert.Equal(ExtractRuleIds(direct), ExtractRuleIds(fromFile));
    }

    private static string[] ExtractRuleIds(string text) =>
        System.Text.RegularExpressions.Regex.Matches(text, @"CRA\d{3}")
            .Select(match => match.Value)
            .ToArray();

    private static string CreateMethodWithSpan(int span)
    {
        var filler = Enumerable.Repeat("        // filler", span - 3);
        return "class C\n{\n    void M()\n    {\n" + string.Join("\n", filler) + "\n    }\n}";
    }
}
