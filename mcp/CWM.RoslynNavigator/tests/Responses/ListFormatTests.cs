using CWM.RoslynNavigator.Responses;

namespace CWM.RoslynNavigator.Tests.Responses;

public class ListFormatTests
{
    private static readonly Paging.Page<int> SmallPage = Paging.Apply<int>([1, 2, 3], maxResults: 10);

    [Fact]
    public void Render_AutoAndFullFits_ReturnsFullForm()
    {
        var json = ListFormat.Render(SmallPage, null, _ => "full", _ => "compact");

        Assert.Equal("full", json);
    }

    [Fact]
    public void Render_AutoAndFullOverBudget_ReturnsCompactForm()
    {
        var huge = new string('x', ResponseBudget.MaxChars + 1);

        var json = ListFormat.Render(SmallPage, "auto", _ => huge, _ => "compact");

        Assert.Equal("compact", json);
    }

    [Theory]
    [InlineData("full", "full")]
    [InlineData("COMPACT", "compact")]
    public void Render_ExplicitFormat_IsHonoured(string format, string expected)
    {
        var json = ListFormat.Render(SmallPage, format, _ => "full", _ => "compact");

        Assert.Equal(expected, json);
    }

    [Fact]
    public void Validate_UnknownFormat_ReturnsInvalidArgument()
    {
        Assert.Contains(ErrorCodes.InvalidArgument, ListFormat.Validate("table"));
        Assert.Null(ListFormat.Validate(null));
        Assert.Null(ListFormat.Validate("Compact"));
    }

    [Fact]
    public void GroupByFile_MixedOrder_GroupsInFirstAppearanceOrder()
    {
        (string File, int Line)[] items = [("b.cs", 1), ("a.cs", 2), ("b.cs", 3)];

        var groups = ListFormat.GroupByFile(items, i => i.File, i => i.Line.ToString());

        Assert.Equal(["b.cs", "a.cs"], groups.Select(g => g.File));
        Assert.Equal(["1", "3"], groups[0].Entries);
        Assert.Equal(items.Length, groups.Sum(g => g.Entries.Count));
    }

    [Fact]
    public void CountBy_MoreKeysThanTop_FoldsRestAndKeepsTotal()
    {
        var items = Enumerable.Range(0, 40).SelectMany(i => Enumerable.Repeat($"P{i:D2}", i + 1)).ToList();

        var counts = ListFormat.CountBy(items, i => i, top: 5);

        Assert.Equal(6, counts.Count);
        Assert.Equal("P39", counts[0].Name);
        Assert.Equal("(35 more)", counts[^1].Name);
        Assert.Equal(items.Count, counts.Sum(c => c.Count));
    }
}
