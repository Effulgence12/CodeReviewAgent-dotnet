using CodeReviewAgent.Core.Tools;

namespace CodeReviewAgent.Tests;

public sealed class FileSystemPluginTests
{
    [Fact]
    public void ListFiles_Default_RecursesAndReturnsOnlyCs()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Z.cs", "class Z { }");
        temp.WriteFile("src/A.cs", "class A { }");
        temp.WriteFile("src/readme.txt", "ignore");
        var plugin = new FileSystemPlugin(temp.Path);

        var result = plugin.ListFiles();

        Assert.Equal($"src/A.cs{Environment.NewLine}Z.cs", result);
    }

    [Fact]
    public void ListFiles_SubdirectoryAndPattern_Filters()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("src/A.cs", "class A { }");
        temp.WriteFile("src/A.generated.cs", "class Generated { }");
        temp.WriteFile("other/B.generated.cs", "class B { }");
        var plugin = new FileSystemPlugin(temp.Path);

        var result = plugin.ListFiles("src", "*.generated.cs");

        Assert.Equal("src/A.generated.cs", result);
    }

    [Fact]
    public void ListFiles_ReturnsStableRelativePaths()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("b.cs", "class B { }");
        temp.WriteFile("A.cs", "class A { }");
        var plugin = new FileSystemPlugin(temp.Path);

        Assert.Equal($"A.cs{Environment.NewLine}b.cs", plugin.ListFiles());
        Assert.DoesNotContain(temp.Path, plugin.ListFiles());
    }

    [Fact]
    public void ListFiles_Traversal_Throws()
    {
        using var temp = new TemporaryDirectory();
        var plugin = new FileSystemPlugin(temp.Path);

        Assert.Throws<UnauthorizedAccessException>(() => plugin.ListFiles(".."));
        Assert.Throws<ArgumentException>(() => plugin.ListFiles(pattern: "../*.cs"));
    }

    [Fact]
    public void ListFiles_EmptyDirectory_ReturnsEmptyMessage()
    {
        using var temp = new TemporaryDirectory();
        var plugin = new FileSystemPlugin(temp.Path);

        Assert.Equal("未找到匹配的 C# 源文件。", plugin.ListFiles());
    }

    [Fact]
    public void ReadFile_AddsOneBasedLineNumbersAndKeepsBlankLines()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("Sample.cs", "first\n\nthird");
        var plugin = new FileSystemPlugin(temp.Path);

        var result = plugin.ReadFile("Sample.cs");

        Assert.Equal($"1: first{Environment.NewLine}2: {Environment.NewLine}3: third", result);
    }

    [Fact]
    public void ReadFile_Utf8BomAndChinese_RoundTrips()
    {
        using var temp = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temp.Path, "中文.cs");
        File.WriteAllText(path, "// 中文\r\nclass 示例 { }", new System.Text.UTF8Encoding(true));
        var plugin = new FileSystemPlugin(temp.Path);

        var result = plugin.ReadFile("中文.cs");

        Assert.Contains("1: // 中文", result);
        Assert.Contains("2: class 示例 { }", result);
    }

    [Fact]
    public void ReadFile_Traversal_Throws()
    {
        using var temp = new TemporaryDirectory();
        var plugin = new FileSystemPlugin(temp.Path);

        Assert.Throws<UnauthorizedAccessException>(() => plugin.ReadFile("../Outside.cs"));
    }

    [Fact]
    public void ReadFile_NonCsFile_Throws()
    {
        using var temp = new TemporaryDirectory();
        temp.WriteFile("notes.txt", "hello");
        var plugin = new FileSystemPlugin(temp.Path);

        Assert.Throws<ArgumentException>(() => plugin.ReadFile("notes.txt"));
    }
}
