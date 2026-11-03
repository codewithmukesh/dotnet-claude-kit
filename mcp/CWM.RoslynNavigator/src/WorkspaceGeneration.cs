using Microsoft.CodeAnalysis.MSBuild;

namespace CWM.RoslynNavigator;

/// <summary>
/// One loaded <see cref="MSBuildWorkspace"/> and the tool calls still reading its snapshot.
///
/// <para>A reload builds the next workspace beside this one and swaps it in. The old
/// workspace cannot be disposed at the swap: a call that started before it may still be
/// compiling the old <c>Solution</c>, and disposing the workspace disposes services (metadata
/// storage, the build host) that snapshot still uses. Each call holds a lease for its
/// duration instead; once the generation is retired and its last lease is released, the
/// workspace is disposed, so a superseded workspace never outlives the calls that use it.</para>
/// </summary>
internal sealed class WorkspaceGeneration(MSBuildWorkspace workspace, Action onDisposed)
{
    private int _leases;
    private int _retired;
    private int _disposed;

    public MSBuildWorkspace Workspace => workspace;

    public bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    /// <summary>
    /// A lease that keeps this workspace alive until disposed, or null when the generation
    /// has already been retired — the caller then leases the current one instead.
    /// </summary>
    public IDisposable? TryAcquire()
    {
        Interlocked.Increment(ref _leases);
        if (Volatile.Read(ref _retired) == 0)
            return new Lease(this);

        Release();
        return null;
    }

    /// <summary>Marks the generation superseded; disposes now if nothing holds a lease.</summary>
    public void Retire()
    {
        Interlocked.Exchange(ref _retired, 1);
        if (Volatile.Read(ref _leases) == 0)
            DisposeOnce();
    }

    /// <summary>Disposes regardless of leases. Shutdown only.</summary>
    public void DisposeNow()
    {
        Interlocked.Exchange(ref _retired, 1);
        DisposeOnce();
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _leases) == 0 && Volatile.Read(ref _retired) == 1)
            DisposeOnce();
    }

    private void DisposeOnce()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        workspace.Dispose();
        onDisposed();
    }

    private sealed class Lease(WorkspaceGeneration generation) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                generation.Release();
        }
    }
}
