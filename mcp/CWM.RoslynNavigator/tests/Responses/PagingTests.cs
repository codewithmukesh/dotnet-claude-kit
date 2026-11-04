using CWM.RoslynNavigator.Responses;

namespace CWM.RoslynNavigator.Tests.Responses;

public class PagingTests
{
    [Fact]
    public void Apply_UnderLimit_ReportsCompleteSet()
    {
        var page = Paging.Apply<int>([1, 2, 3], maxResults: 10);

        Assert.Equal(3, page.Count);
        Assert.Equal(3, page.TotalFound);
        Assert.False(page.Truncated);
        Assert.Equal(10, page.Limit);
    }

    [Fact]
    public void Apply_ExactlyAtLimit_IsNotTruncated()
    {
        // The boundary that a naive "Count >= Limit" check gets wrong.
        var page = Paging.Apply<int>([1, 2, 3], maxResults: 3);

        Assert.Equal(3, page.Count);
        Assert.False(page.Truncated);
    }

    [Fact]
    public void Apply_OverLimit_TruncatesAndKeepsTotal()
    {
        var page = Paging.Apply<int>([1, 2, 3, 4, 5], maxResults: 2);

        Assert.Equal([1, 2], page.Items);
        Assert.Equal(2, page.Count);
        Assert.Equal(5, page.TotalFound);
        Assert.True(page.Truncated);
        Assert.Equal(2, page.Limit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Apply_NonPositiveLimit_ClampsToOne(int maxResults)
    {
        var page = Paging.Apply<int>([1, 2, 3], maxResults);

        Assert.Equal(1, page.Limit);
        Assert.Single(page.Items);
        Assert.True(page.Truncated);
    }

    [Fact]
    public void Apply_EmptySource_IsNotTruncated()
    {
        var page = Paging.Apply<int>([], maxResults: 5);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalFound);
        Assert.False(page.Truncated);
    }

    [Fact]
    public void Apply_Offset_ReturnsPageAfterItWithNextOffset()
    {
        var page = Paging.Apply<int>([1, 2, 3, 4, 5], maxResults: 2, offset: 2);

        Assert.Equal([3, 4], page.Items);
        Assert.Equal(2, page.Offset);
        Assert.Equal(4, page.NextOffset);
        Assert.True(page.Truncated);
        Assert.Equal(5, page.TotalFound);
    }

    [Fact]
    public void Apply_LastPage_HasNoNextOffset()
    {
        var page = Paging.Apply<int>([1, 2, 3, 4, 5], maxResults: 2, offset: 4);

        Assert.Equal([5], page.Items);
        Assert.False(page.Truncated);
        Assert.Null(page.NextOffset);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(99)]
    public void Apply_OffsetAtOrPastEnd_ReturnsEmptyCompletePage(int offset)
    {
        var page = Paging.Apply<int>([1, 2, 3, 4, 5], maxResults: 2, offset);

        Assert.Empty(page.Items);
        Assert.Equal(5, page.TotalFound);
        Assert.Equal(5, page.Offset);
        Assert.False(page.Truncated);
        Assert.Null(page.NextOffset);
    }

    [Fact]
    public void Apply_NegativeOffset_StartsAtZero()
    {
        var page = Paging.Apply<int>([1, 2, 3], maxResults: 2, offset: -3);

        Assert.Equal([1, 2], page.Items);
        Assert.Equal(0, page.Offset);
    }

    [Fact]
    public void Apply_WalkingNextOffset_VisitsEveryItemOnce()
    {
        // NextOffset stability: following it from 0 must cover the list with no gap or overlap.
        List<int> all = [.. Enumerable.Range(0, 23)];
        var seen = new List<int>();
        int? offset = 0;

        while (offset is { } next)
        {
            var page = Paging.Apply(all, maxResults: 5, offset: next);
            seen.AddRange(page.Items);
            offset = page.NextOffset;
        }

        Assert.Equal(all, seen);
    }

    [Fact]
    public void Fit_OverBudget_KeepsLargestPrefixAndPointsNextOffsetAtTheRest()
    {
        var page = Paging.Apply<string>([.. Enumerable.Range(0, 100).Select(i => $"item{i:D3}")], maxResults: 100, offset: 10);

        var json = Paging.Fit(page, p => string.Join(",", p.Items) + $"|{p.NextOffset}", budget: 100);

        var (items, next) = (json.Split('|')[0].Split(','), json.Split('|')[1]);
        Assert.True(json.Length <= 100);
        Assert.Equal("item010", items[0]);
        Assert.Equal((10 + items.Length).ToString(), next);
    }

    [Fact]
    public void Fit_UnderBudget_ReturnsWholePage()
    {
        var page = Paging.Apply<int>([1, 2, 3], maxResults: 10);

        var json = Paging.Fit(page, p => string.Join(",", p.Items), budget: 100);

        Assert.Equal("1,2,3", json);
    }

    [Fact]
    public void FitCount_NothingFits_KeepsOneEntry()
    {
        var json = Paging.FitCount(50, count => new string('x', count * 100), budget: 10);

        Assert.Equal(100, json.Length);
    }

    [Fact]
    public void Empty_RequestedLimit_ReportsNoResultsAtThatLimit()
    {
        var page = Paging.Empty<int>(25);

        Assert.Empty(page.Items);
        Assert.False(page.Truncated);
        Assert.Equal(25, page.Limit);
    }
}
