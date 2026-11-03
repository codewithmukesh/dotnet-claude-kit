using CWM.RoslynNavigator.Responses;

namespace CWM.RoslynNavigator.Tests.Responses;

public class ToolJsonTests
{
    [Fact]
    public void Serialize_NullOptionalFields_OmitsThem()
    {
        // Act
        var json = ToolJson.Serialize(new ErrorResponse(ErrorCodes.SymbolNotFound, "missing"));

        // Assert
        Assert.Equal("""{"Error":"SymbolNotFound","Message":"missing"}""", json);
    }

    [Fact]
    public void Serialize_ApostropheAndNonAscii_WritesThemUnescaped()
    {
        // Act — the default encoder writes ' as ', six characters for one.
        var json = ToolJson.Serialize(new ErrorResponse(ErrorCodes.FileNotFound, "File 'Café.cs' not found"));

        // Assert
        Assert.Contains("File 'Café.cs' not found", json);
    }

    [Fact]
    public void Serialize_IsGeneratedFalse_OmitsIt()
    {
        // Act
        var plain = ToolJson.Serialize(new ImplementationInfo("Order", "Order.cs", 3, IsGenerated: false));
        var generated = ToolJson.Serialize(new ImplementationInfo("Order", "Order.g.cs", 3, IsGenerated: true));

        // Assert
        Assert.DoesNotContain("IsGenerated", plain);
        Assert.Contains("\"IsGenerated\":true", generated);
    }

    [Fact]
    public void Context_EveryTopLevelResponseType_IsRegistered()
    {
        // Arrange — a type missing from ToolJsonContext fails at the first call of its tool.
        var responses = typeof(ToolJson).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(ToolJson).Namespace
                && (t.Name.EndsWith("Result", StringComparison.Ordinal) || t.Name is "ErrorResponse" or "StatusResponse" or "SymbolDetail"))
            .ToList();

        // Assert
        Assert.NotEmpty(responses);
        Assert.All(responses, t => Assert.NotNull(ToolJsonContext.Default.GetTypeInfo(t)));
    }
}
