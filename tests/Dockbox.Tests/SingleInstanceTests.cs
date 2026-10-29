using Dockbox.Services;

namespace Dockbox.Tests;

public sealed class SingleInstanceTests
{
    [Theory]
    [InlineData("Dockbox", new[] { "Dockbox" })]
    [InlineData("dockbox", new[] { "dockbox" })]
    [InlineData("Dockbox (1)", new[] { "Dockbox (1)", "Dockbox" })]
    [InlineData("", new[] { "Dockbox" })]
    public void NomesProcurados_IncluemOExeRenomeado(string current, string[] expected) =>
        Assert.Equal(expected, SingleInstance.CandidateNames(current));

}
