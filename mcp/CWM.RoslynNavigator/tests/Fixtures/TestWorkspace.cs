using Microsoft.Extensions.Logging.Abstractions;

namespace CWM.RoslynNavigator.Tests.Fixtures;

/// <summary>
/// A private <see cref="WorkspaceManager"/> for tests that mutate workspace state (reloads,
/// file edits) and so cannot share the SampleSolution collection's instance. Registers
/// MSBuild first, which every direct construction needs.
/// </summary>
public static class TestWorkspace
{
    public static WorkspaceManager Create(TimeProvider clock)
    {
        TestSolutionFixture.RegisterMSBuild();
        return new WorkspaceManager(NullLogger<WorkspaceManager>.Instance, clock);
    }
}
