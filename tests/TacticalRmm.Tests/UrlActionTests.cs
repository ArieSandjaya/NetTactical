using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class UrlActionTests
{
    [Theory]
    [InlineData("Tickets", "https://t.example.com/s?q={{agent.hostname}}", null)]
    [InlineData("Tickets", "http://t.example.com/{{ client.name }}/{{site.id}}", null)]
    [InlineData("", "https://x.com", "Give the action a name.")]
    [InlineData("T", "", "Enter the URL.")]
    [InlineData("T", "ftp://x.com", "The URL must start with http:// or https://.")]
    [InlineData("T", "javascript:alert(1)", "The URL must start with http:// or https://.")]
    [InlineData("T", "{{agent.hostname}}://x.com", "The URL must start with http:// or https://.")]
    [InlineData("T", "https://x.com/a b", "The URL must not contain spaces.")]
    public void Validates(string name, string pattern, string? expected) => Assert.Equal(expected, UrlActions.Validate(name, pattern));

    [Fact]
    public void RejectsUnknownPlaceholder() => Assert.Contains("Unknown placeholder {{agent.password}}", UrlActions.Validate("T", "https://x.com/{{agent.password}}"));

    [Fact]
    public void RenderEncodesValuesAndBlanksMissingOnes()
    {
        var v = new Dictionary<string, string?> { ["agent.hostname"] = "PC 1&x=2/../", ["site.name"] = null };
        Assert.Equal("https://x.com/?q=PC%201%26x%3D2%2F..%2F&s=", UrlActions.Render("https://x.com/?q={{agent.hostname}}&s={{ Site.Name }}", k => v.GetValueOrDefault(k)));
    }
}
