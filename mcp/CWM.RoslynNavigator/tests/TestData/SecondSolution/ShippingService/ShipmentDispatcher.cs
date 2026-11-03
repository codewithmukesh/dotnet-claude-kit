using SampleDomain;

namespace ShippingService;

/// <summary>
/// Lives in a second solution and depends on SampleDomain, which SampleSolution also owns.
/// </summary>
public sealed class ShipmentDispatcher(IOrderRepository orders)
{
    public async Task<bool> DispatchAsync(Guid orderId, CancellationToken ct = default)
    {
        var order = await orders.GetByIdAsync(orderId, ct);
        if (order is null)
            return false;

        order.Ship();
        await orders.UpdateAsync(order, ct);
        return true;
    }
}
