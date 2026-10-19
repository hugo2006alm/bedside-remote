namespace BedsideRemote.Agent;

// Each connection owns its drag. Lost connections and missing heartbeats release it.
internal sealed class PointerDrag(Action<uint, int, int> emit, Func<IntPtr> foreground, int leaseMilliseconds = 2000)
{
    private readonly object sync = new();
    private CancellationToken? owner;
    private CancellationTokenRegistration cancellation;
    private System.Threading.Timer? lease;
    private long expires;
    private IntPtr window;
    public bool Active { get { lock (sync) return owner != null; } }
    public void Handle(string phase, int dx, int dy, CancellationToken token)
    {
        lock (sync)
        {
            token.ThrowIfCancellationRequested();
            if (owner != null && owner != token) throw new InvalidOperationException("Another connection is dragging. Wait for it to release.");
            if (phase == "end") { Release(); return; }
            if (owner == null)
            {
                if (phase != "start") throw new InvalidOperationException("Drag expired. Lift your finger and start again.");
                window = foreground();
                if (window == IntPtr.Zero) throw new InvalidOperationException("Focus a window before dragging.");
                emit(2, 0, 0);
                owner = token;
                cancellation = token.UnsafeRegister(_ => { lock (sync) { if (owner == token) Release(); } }, null);
                if (owner == null) return;
            }
            if (foreground() != window) { Release(); throw new InvalidOperationException("Drag stopped because the focused window changed."); }
            try
            {
                if (dx != 0 || dy != 0) emit(1, dx, dy);
                expires = Environment.TickCount64 + leaseMilliseconds;
                lease ??= new System.Threading.Timer(_ =>
                {
                    lock (sync)
                    {
                        if (owner == null) return;
                        var remaining = expires - Environment.TickCount64;
                        // Timer and TickCount64 can cross a clock tick differently.
                        // An early callback must schedule the remaining lease, not lose it.
                        if (remaining > 0) lease!.Change((int)Math.Min(remaining, int.MaxValue), Timeout.Infinite);
                        else Release();
                    }
                });
                lease.Change(leaseMilliseconds, Timeout.Infinite);
            }
            catch { Release(); throw; }
        }
    }
    public void Release()
    {
        lock (sync)
        {
            if (owner == null) return;
            owner = null;
            cancellation.Unregister();
            lease?.Change(Timeout.Infinite, Timeout.Infinite);
            try { emit(4, 0, 0); }
            catch (InvalidOperationException) { /* Best effort if Windows rejects cleanup. */ }
        }
    }
}
