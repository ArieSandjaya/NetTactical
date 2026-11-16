using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class CustomFieldRulesTests
{
    [Fact]
    public void BlankValueMeansNoValueForEveryType()
    {
        foreach (var t in CustomFieldRules.Types)
            Assert.Equal((true, "", (string?)null), CustomFieldRules.Normalize(t, "a\nb", "  "));
    }

    [Fact]
    public void NumbersAcceptDotAndRejectLetters()
    {
        Assert.Equal("12.5", CustomFieldRules.Normalize("number", null, " 12.5 ").Value);
        Assert.Equal("3", CustomFieldRules.Normalize("number", null, "3").Value);
        Assert.False(CustomFieldRules.Normalize("number", null, "12abc").Ok);
    }

    [Fact]
    public void CheckboxIsTrueOrFalseOnly()
    {
        Assert.Equal("true", CustomFieldRules.Normalize("checkbox", null, "TRUE").Value);
        Assert.Equal("false", CustomFieldRules.Normalize("checkbox", null, "false").Value);
        Assert.False(CustomFieldRules.Normalize("checkbox", null, "yes").Ok);
    }

    [Fact]
    public void DateTimeIsStoredWithoutSecondsOrZone()
    {
        Assert.Equal("2026-11-16T07:50", CustomFieldRules.Normalize("datetime", null, "2026-11-16T07:50:33").Value);
        Assert.False(CustomFieldRules.Normalize("datetime", null, "tomorrow-ish").Ok);
    }

    [Fact]
    public void SingleChoiceMustBeOneOfTheOptions()
    {
        Assert.True(CustomFieldRules.Normalize("single", "Gold\nSilver", "Gold").Ok);
        Assert.False(CustomFieldRules.Normalize("single", "Gold\nSilver", "Bronze").Ok);
    }

    [Fact]
    public void TextIsLimited()
    {
        Assert.True(CustomFieldRules.Normalize("text", null, new string('a', 2000)).Ok);
        Assert.False(CustomFieldRules.Normalize("text", null, new string('a', 2001)).Ok);
    }

    [Fact]
    public void OptionsAreTrimmedAndDeduplicated()
    {
        Assert.Equal(new[] { "A", "B" }, CustomFieldRules.ParseOptions(" A \n\nB\nA\r\n"));
    }

    [Fact]
    public void DefinitionIsChecked()
    {
        Assert.Null(CustomFieldRules.CheckDefinition("Owner", "agent", "text", null, null));
        Assert.NotNull(CustomFieldRules.CheckDefinition(" ", "agent", "text", null, null));
        Assert.NotNull(CustomFieldRules.CheckDefinition("X", "user", "text", null, null));
        Assert.NotNull(CustomFieldRules.CheckDefinition("X", "agent", "colour", null, null));
        Assert.NotNull(CustomFieldRules.CheckDefinition("X", "agent", "single", "  ", null));
        Assert.NotNull(CustomFieldRules.CheckDefinition("X", "agent", "number", null, "abc"));
        Assert.Null(CustomFieldRules.CheckDefinition("X", "agent", "single", "a\nb", "b"));
    }
}
