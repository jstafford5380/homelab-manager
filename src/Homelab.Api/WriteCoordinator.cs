namespace Homelab.Api;

/// <summary>One serialized writer for this single-process SQLite deployment.</summary>
public sealed class WriteCoordinator
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async ValueTask<IDisposable> EnterAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        return new Lease(gate);
    }
    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int disposed;
        public void Dispose() { if (Interlocked.Exchange(ref disposed, 1) == 0) gate.Release(); }
    }
}
