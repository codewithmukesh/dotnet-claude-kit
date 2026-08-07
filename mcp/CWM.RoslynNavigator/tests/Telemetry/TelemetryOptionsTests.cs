using CWM.RoslynNavigator.Telemetry;

namespace CWM.RoslynNavigator.Tests.Telemetry;

public class TelemetryOptionsTests
{
    private static TelemetryOptions Resolve(string? value) =>
        TelemetryOptions.FromEnvironment(_ => value, localAppDataOverride: "/tmp/x");

    [Theory]
    [InlineData("on")]
    [InlineData("ON")]
    [InlineData(" true ")]
    [InlineData("1")]
    [InlineData("yes")]
    [InlineData("enabled")]
    public void FromEnvironment_AffirmativeValue_EnablesCollection(string value)
    {
        Assert.True(Resolve(value).Enabled);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("off")]
    [InlineData("false")]
    [InlineData("0")]
    [InlineData("maybe")]
    public void FromEnvironment_AnythingElse_LeavesCollectionOff(string? value)
    {
        // Opt-in: an unset or unrecognized value must never start collecting. This is a
        // published package, and the default has to be the conservative one.
        Assert.False(Resolve(value).Enabled);
    }

    [Fact]
    public void FromEnvironment_UnsetVariable_IsOffByDefault()
    {
        var options = TelemetryOptions.FromEnvironment(_ => null, localAppDataOverride: "/tmp/x");

        Assert.False(options.Enabled);
    }
}

public class TokenEstimatorTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-5, 0)]
    [InlineData(3500, 1000)]
    [InlineData(35, 10)]
    [InlineData(1, 0)]
    public void FromBytes_AppliesDivisor(long bytes, int expected)
    {
        Assert.Equal(expected, TokenEstimator.FromBytes(bytes));
    }

    [Fact]
    public void FromBytes_HugeInput_SaturatesInsteadOfWrapping()
    {
        Assert.Equal(int.MaxValue, TokenEstimator.FromBytes(long.MaxValue));
    }

    [Fact]
    public void FromText_UsesUtf8ByteLength()
    {
        // A multi-byte identifier must not be undercounted by measuring characters.
        var text = new string('é', 350); // 700 UTF-8 bytes

        Assert.Equal(200, TokenEstimator.FromText(text));
    }

    [Fact]
    public void Divisor_IsMarkedUncalibrated()
    {
        // Guards the honesty of the public claim: if someone calibrates the divisor they
        // must flip this flag, and if they flip it without calibrating this test is where
        // the reviewer will ask why the sample size is still zero.
        Assert.False(TokenEstimator.IsCalibrated);
        Assert.Equal(0, TokenEstimator.CalibrationSampleSize);
    }
}
