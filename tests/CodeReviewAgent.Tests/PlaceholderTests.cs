using Xunit;

namespace CodeReviewAgent.Tests;

/// <summary>Minimal test-project wiring check; substantive Part B tests live in feature-specific files.</summary>
public class TestProjectSmokeTests
{
    [Fact]
    public void TestAssembly_Loads()
    {
        Assert.True(true);
    }
}
