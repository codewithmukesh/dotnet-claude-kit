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
    public void Empty_ReportsNoResultsAtRequestedLimit()
    {
        var page = Paging.Empty<int>(25);

        Assert.Empty(page.Items);
        Assert.False(page.Truncated);
        Assert.Equal(25, page.Limit);
    }
}
