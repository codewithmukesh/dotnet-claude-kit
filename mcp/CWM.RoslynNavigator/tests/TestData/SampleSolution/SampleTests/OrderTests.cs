using SampleDomain;
using Xunit;

namespace SampleTests;

/// <summary>
/// One test class per production type: get_test_coverage_map matches it to Order.
/// DO NOT rename — the class name is what drives the match.
/// </summary>
public class OrderTests
{
    [Fact]
    public void Create_TwoItems_SumsTotal()
    {
        var order = Order.Create("customer-1", [new OrderItem("Pen", 2, 1.5m), new OrderItem("Ink", 1, 3m)], DateTime.UnixEpoch);

        Assert.Equal(6m, order.Total);
    }

    [Fact]
    public void Cancel_PendingOrder_SetsCancelled()
    {
        var order = Order.Create("customer-1", [], DateTime.UnixEpoch);

        order.Cancel();

        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void Create_QuantityOfOneItem_ScalesTotal(int quantity)
    {
        var order = Order.Create("customer-1", [new OrderItem("Pen", quantity, 2m)], DateTime.UnixEpoch);

        Assert.Equal(quantity * 2m, order.Total);
    }
}
