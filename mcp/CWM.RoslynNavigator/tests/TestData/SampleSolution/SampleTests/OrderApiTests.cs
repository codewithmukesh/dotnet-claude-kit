using Microsoft.AspNetCore.Mvc.Testing;
using SampleApi;
using Xunit;

namespace SampleTests;

/// <summary>
/// Integration-shaped test: drives the API through WebApplicationFactory rather than one
/// class per type, so it matches no production type by name.
/// </summary>
public class OrderApiTests(WebApplicationFactory<OrderService> factory) : IClassFixture<WebApplicationFactory<OrderService>>
{
    [Fact]
    public async Task GetOrder_UnknownId_ReturnsNotFound()
    {
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/orders/{Guid.Empty}", CancellationToken.None);

        Assert.Equal(404, (int)response.StatusCode);
    }
}
