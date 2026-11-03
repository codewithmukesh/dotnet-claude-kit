using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// A throwaway one-project solution with the shapes SampleSolution lacks: an abstract method
/// with three overrides and three DI registrations. Kept out of SampleSolution so the many
/// tests that pin its exact type, reference and dead-code counts stay untouched.
/// </summary>
public sealed class FeatureSolutionFixture : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "cwm-rn-features-" + Guid.NewGuid().ToString("N"));

    public WorkspaceManager WorkspaceManager { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        TestSolutionFixture.RegisterMSBuild();
        TestDataCopy.WriteGuardFiles(_root);

        var project = Directory.CreateDirectory(Path.Combine(_root, "Features")).FullName;
        await File.WriteAllTextAsync(Path.Combine(project, "Features.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
                <Nullable>enable</Nullable>
                <ImplicitUsings>enable</ImplicitUsings>
              </PropertyGroup>
            </Project>
            """);

        await File.WriteAllTextAsync(Path.Combine(project, "Shapes.cs"), """
            namespace Features;

            public abstract class Shape
            {
                public abstract double Area();
            }

            public sealed class Circle(double radius) : Shape
            {
                public override double Area() => Math.PI * radius * radius;
            }

            public sealed class Square(double side) : Shape
            {
                public override double Area() => side * side;
            }

            public sealed class Triangle(double b, double h) : Shape
            {
                public override double Area() => b * h / 2;
            }
            """);

        await File.WriteAllTextAsync(Path.Combine(project, "Startup.cs"), """
            namespace Features;

            public interface IClock;
            public sealed class SystemClock : IClock;
            public interface IRepository;
            public sealed class Repository : IRepository;
            public interface IMailer;
            public sealed class Mailer : IMailer;

            // Microsoft.Extensions.DependencyInjection is matched by method name, so a local
            // stand-in keeps this solution free of package references.
            public sealed class ServiceCollection
            {
                public ServiceCollection AddSingleton<TService, TImplementation>() => this;
                public ServiceCollection AddScoped<TService, TImplementation>() => this;
                public ServiceCollection AddTransient<TService, TImplementation>() => this;
            }

            public static class Startup
            {
                public static void Register(ServiceCollection services)
                {
                    services.AddSingleton<IClock, SystemClock>();
                    services.AddScoped<IRepository, Repository>();
                    services.AddTransient<IMailer, Mailer>();
                }
            }
            """);

        var solutionPath = Path.Combine(_root, "Features.slnx");
        await File.WriteAllTextAsync(solutionPath, """
            <Solution>
              <Project Path="Features/Features.csproj" />
            </Solution>
            """);

        WorkspaceManager = new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, TimeProvider.System);
        await WorkspaceManager.LoadSolutionAsync(solutionPath);
        await WorkspaceManager.WaitForWarmUpAsync();
    }

    public ValueTask DisposeAsync()
    {
        WorkspaceManager?.Dispose();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temp folder; leaving it behind only costs disk space.
        }

        return ValueTask.CompletedTask;
    }
}
