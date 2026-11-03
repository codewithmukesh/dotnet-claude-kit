// Stand-ins for xunit.v3 and Microsoft.AspNetCore.Mvc.Testing: the same namespaces, names and
// shapes the tests use, because TestData is loaded without a NuGet restore.

namespace Xunit
{
    [AttributeUsage(AttributeTargets.Method)]
    public class FactAttribute : Attribute;

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class TheoryAttribute : FactAttribute;

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class InlineDataAttribute(params object[] data) : Attribute
    {
        public object[] Data { get; } = data;
    }

    public interface IClassFixture<TFixture> where TFixture : class;

    public static class Assert
    {
        public static void Equal<T>(T expected, T actual)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new InvalidOperationException($"Expected {expected}, got {actual}.");
        }
    }
}

namespace Microsoft.AspNetCore.Mvc.Testing
{
    public class WebApplicationFactory<TEntryPoint> : IDisposable where TEntryPoint : class
    {
        public HttpClient CreateClient() =>
            throw new NotSupportedException("Stub: TestData is never restored or run.");

        public void Dispose() => GC.SuppressFinalize(this);
    }
}
