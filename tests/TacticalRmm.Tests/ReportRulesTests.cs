using TacticalRmm.Application.Logic;
using Xunit;

namespace TacticalRmm.Tests;

public class ReportRulesTests
{
    [Fact]
    public void PlainTextIsUnchangedAndSpecialCharactersAreQuoted()
    {
        Assert.Equal("PLZ-SRV-01", ReportRules.Cell("PLZ-SRV-01"));
        Assert.Equal("\"a,b\"", ReportRules.Cell("a,b"));
        Assert.Equal("\"say \"\"hi\"\"\"", ReportRules.Cell("say \"hi\""));
        Assert.Equal("\"line1\nline2\"", ReportRules.Cell("line1\nline2"));
        Assert.Equal("", ReportRules.Cell(null));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://x\")")]
    [InlineData("+cmd|' /C calc'!A0")]
    [InlineData("-2+3")]
    [InlineData("@SUM(A1)")]
    public void TextThatCouldRunAsAFormulaGetsAnApostrophe(string evil)
    {
        Assert.StartsWith("'", ReportRules.Cell(evil).TrimStart('"'));
    }

    [Fact]
    public void NegativeNumbersStayNumbers()
    {
        Assert.Equal("-5", ReportRules.Cell("-5"));
        Assert.Equal("12.5", ReportRules.Cell("12.5"));
    }

    [Fact]
    public void CsvHasHeaderAndRowsWithCrLf()
    {
        var csv = ReportRules.Csv(["A", "B"], [["1", "x,y"], ["2", null]]);
        Assert.Equal("A,B\r\n1,\"x,y\"\r\n2,\r\n", csv);
    }

    [Fact]
    public void DefaultRangeIsTheLast30DaysIncludingToday()
    {
        var (f, t, err) = ReportRules.Range(null, null, new DateTime(2026, 11, 16, 8, 0, 0, DateTimeKind.Utc));
        Assert.Null(err);
        Assert.Equal(new DateTime(2026, 10, 17), f);
        Assert.Equal(new DateTime(2026, 11, 17), t);
        Assert.Equal(DateTimeKind.Utc, f.Kind);   // PostgreSQL only accepts UTC
        Assert.Equal(DateTimeKind.Utc, t.Kind);
    }

    [Fact]
    public void GivenRangeIsInclusiveOfTheLastDay()
    {
        var (f, t, err) = ReportRules.Range("2026-11-01", "2026-11-03", DateTime.UtcNow);
        Assert.Null(err);
        Assert.Equal(new DateTime(2026, 11, 1), f);
        Assert.Equal(new DateTime(2026, 11, 4), t);
    }

    [Theory]
    [InlineData("11/01/2026", null)]
    [InlineData("2026-11-05", "2026-11-01")]
    [InlineData("2025-01-01", "2026-11-01")]
    [InlineData(null, "tomorrow")]
    public void BadRangesAreRefused(string? from, string? to)
    {
        Assert.NotNull(ReportRules.Range(from, to, new DateTime(2026, 11, 16)).Error);
    }
}
