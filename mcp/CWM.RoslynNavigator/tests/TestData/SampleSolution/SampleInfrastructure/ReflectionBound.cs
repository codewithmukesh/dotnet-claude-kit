namespace SampleInfrastructure;

/// <summary>
/// Never referenced by name — resolved at runtime from the string literal in
/// <see cref="PluginLoader"/>. A reference search sees zero usages, so dead-code
/// detection must downgrade it rather than call it removable.
/// </summary>
internal sealed class LegacyPricingPlugin
{
    public decimal Apply(decimal amount) => amount * 0.9m;
}

/// <summary>
/// Genuinely unreferenced with no reflection signal anywhere — the control case that
/// should still be reported at high confidence.
/// </summary>
internal sealed class TrulyUnusedCalculator
{
    public decimal Apply(decimal amount) => amount;
}

internal static class PluginLoader
{
    private const string PluginTypeName = "SampleInfrastructure.LegacyPricingPlugin";

    public static object? Load() => Type.GetType(PluginTypeName) is { } t
        ? Activator.CreateInstance(t)
        : null;
}
