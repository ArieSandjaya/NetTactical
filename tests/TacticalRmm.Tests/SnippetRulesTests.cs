using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class SnippetRulesTests
{
    private static Dictionary<string, string> S(params (string, string)[] x) => x.ToDictionary(a => a.Item1, a => a.Item2);

    [Fact]
    public void NamesAreSimple()
    {
        Assert.Null(SnippetRules.CheckName("log-helper_2"));
        Assert.NotNull(SnippetRules.CheckName("has space"));
        Assert.NotNull(SnippetRules.CheckName(""));
        Assert.NotNull(SnippetRules.CheckName(null));
        Assert.NotNull(SnippetRules.CheckName(new string('a', 65)));
    }

    [Fact]
    public void FindsEachNameOnceWhateverTheSpacing()
    {
        Assert.Equal(new[] { "a", "b" }, SnippetRules.Names("{{a}} x {{ b }} y {{a}}"));
        Assert.Empty(SnippetRules.Names("no placeholders { here }"));
        Assert.Empty(SnippetRules.Names(null));
    }

    [Fact]
    public void ReplacesKnownNamesAndKeepsUnknownOnes()
    {
        var (body, err) = SnippetRules.Expand("a {{x}} b {{ y }} c {{unknown}}", S(("x", "ONE"), ("y", "TWO")));
        Assert.Null(err);
        Assert.Equal("a ONE b TWO c {{unknown}}", body);
    }

    [Fact]
    public void ASnippetThatRefersToItselfDoesNotLoop()
    {
        var (body, err) = SnippetRules.Expand("{{a}}", S(("a", "x {{a}} y")));
        Assert.Null(err);
        Assert.Equal("x {{a}} y", body);   // one pass: the inner placeholder stays as text
    }

    [Fact]
    public void SnippetCodeIsInsertedAsIsEvenWithDollarSigns()
    {
        var (body, _) = SnippetRules.Expand("{{a}}", S(("a", "$1 $& ${x}")));
        Assert.Equal("$1 $& ${x}", body);
    }

    [Fact]
    public void NothingToReplaceReturnsTheBodyUntouched()
    {
        Assert.Equal(("echo hi", (string?)null), SnippetRules.Expand("echo hi", S(("a", "x"))));
        Assert.Equal(("", (string?)null), SnippetRules.Expand(null, S()));
    }

    [Fact]
    public void TooLargeResultIsRefused()
    {
        var big = new string('x', SnippetRules.MaxCode);
        var body = string.Join("", Enumerable.Repeat("{{a}}", 11));
        var (result, err) = SnippetRules.Expand(body, S(("a", big)));
        Assert.NotNull(err);
        Assert.Equal(body, result);
    }

    [Fact]
    public void CodeIsChecked()
    {
        Assert.Null(SnippetRules.CheckCode("Write-Host hi"));
        Assert.NotNull(SnippetRules.CheckCode("  "));
        Assert.NotNull(SnippetRules.CheckCode(new string('a', 100_001)));
    }
}
