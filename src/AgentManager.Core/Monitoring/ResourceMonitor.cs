using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AgentManager.Core.Monitoring;

/// <summary>One sampling instant of host resource usage. Percent values are 0..100, -1 = unknown.
/// Network rates are bytes/sec (>=0). <see cref="GpuAvailable"/> is false when no GPU performance
/// counter could be opened (no supported GPU / counters disabled).</summary>
public sealed record ResourceSnapshot(
    double CpuPercent,
    double GpuPercent,
    double MemoryPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    double NetRecvBytesPerSec,
    double NetSentBytesPerSec,
    bool GpuAvailable)
{
    public static ResourceSnapshot Empty { get; } = new(-1, -1, 0, 0, 0, 0, 0, false);
}

/// <summary>Headless host resource monitor: samples CPU / GPU / RAM / Ethernet once per second on a
/// background thread and raises <see cref="Sampled"/> with a fresh <see cref="ResourceSnapshot"/>.
/// Holds no UI types — callers format snapshots for display. Lives in Core (alongside the Win32
/// <c>ConPtyHost</c>) so the headless smoke harness can exercise it; it is Windows-only (Win32
/// <c>GlobalMemoryStatusEx</c> + GPU Engine performance counters).
///
/// Cost: a single ~1 Hz threadpool tick doing one <c>% Processor Time</c> read, a handful of GPU engine
/// counter reads (summed for the first physical GPU), a few network-adapter reads, and one memory P/Invoke
/// — well under 1% CPU and a few KB of state. Counters are created once (seeded) and reused; each
/// <c>NextValue</c> is O(1). Everything is guarded so a missing counter/category degrades to "—"
/// instead of throwing.</summary>
[SupportedOSPlatform("windows")]
public sealed class ResourceMonitor : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private readonly object _gate = new();

    // CPU: single % Processor Time (_Total) counter.
    private PerformanceCounter? _cpu;
    // GPU: one Utilization Percentage counter per "phys_0" engine node; null when no GPU category.
    private List<PerformanceCounter>? _gpuEngines;
    private bool _gpuChecked;
    // Network: per-adapter receive/send rate counters.
    private readonly List<PerformanceCounter> _netRx = new();
    private readonly List<PerformanceCounter> _netTx = new();

    /// <summary>Raised on a background thread with a fresh snapshot. Callers must marshal to the UI thread.</summary>
    public event Action<ResourceSnapshot>? Sampled;

    public ResourceMonitor()
        => _timer = new System.Threading.Timer(_ => SampleSafe(), null, Timeout.Infinite, Timeout.Infinite);

    public void Start() => _timer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    /// <summary>Synchronous one-shot sample (test entry point): lazily creates/seeds counters, then reads
    /// them. The first call seeds and returns ~0; a second call (>=1s later) returns real values.</summary>
    public ResourceSnapshot SampleOnce() => Sample();

    private void SampleSafe()
    {
        try { Sampled?.Invoke(Sample()); }
        catch { /* never let background sampling crash the host */ }
    }

    private ResourceSnapshot Sample()
    {
        lock (_gate)
        {
            EnsureCounters();

            // First NextValue after creation/seed returns ~0; real values appear from the 2nd tick onward.
            var cpu = _cpu?.NextValue() ?? -1;

            double gpu = -1;
            if (_gpuEngines is { Count: > 0 })
            {
                var sum = 0.0;
                foreach (var c in _gpuEngines) sum += c.NextValue();
                gpu = Math.Clamp(sum, 0, 100);
            }

            ReadMemoryStatus(out var mem);

            var rx = 0.0;
            var tx = 0.0;
            foreach (var c in _netRx) rx += c.NextValue();
            foreach (var c in _netTx) tx += c.NextValue();

            return new ResourceSnapshot(
                CpuPercent: cpu,
                GpuPercent: gpu,
                MemoryPercent: mem.dwMemoryLoad,
                MemoryUsedBytes: mem.ullTotalPhys - mem.ullAvailPhys,
                MemoryTotalBytes: mem.ullTotalPhys,
                NetRecvBytesPerSec: rx,
                NetSentBytesPerSec: tx,
                GpuAvailable: gpu >= 0);
        }
    }

    /// <summary>Lazy-create + seed all counters on the first sample. Idempotent under <c>_gate</c>.</summary>
    private void EnsureCounters()
    {
        if (_cpu is null)
        {
            try
            {
                _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", readOnly: true);
                _cpu.NextValue(); // seed
            }
            catch { _cpu = null; }
        }

        if (!_gpuChecked)
        {
            _gpuChecked = true;
            try
            {
                var cat = new PerformanceCounterCategory("GPU Engine");
                var engines = new List<PerformanceCounter>();
                // Instance names look like: pid_0_luid_0x..._phys_0_eng_0_engtype_3D — first physical GPU only.
                foreach (var inst in cat.GetInstanceNames())
                {
                    if (!inst.Contains("phys_0", StringComparison.OrdinalIgnoreCase)) continue;
                    var c = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst, readOnly: true);
                    c.NextValue(); // seed
                    engines.Add(c);
                }
                _gpuEngines = engines.Count > 0 ? engines : null;
            }
            catch { _gpuEngines = null; }
        }

        if (_netRx.Count == 0 && _netTx.Count == 0)
        {
            try
            {
                var cat = new PerformanceCounterCategory("Network Interface");
                foreach (var inst in cat.GetInstanceNames())
                {
                    var rx = new PerformanceCounter("Network Interface", "Bytes Received/sec", inst, readOnly: true);
                    var tx = new PerformanceCounter("Network Interface", "Bytes Sent/sec", inst, readOnly: true);
                    rx.NextValue(); tx.NextValue(); // seed
                    _netRx.Add(rx); _netTx.Add(tx);
                }
            }
            catch { /* no network category on this host */ }
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        _cpu?.Dispose();
        if (_gpuEngines is not null) foreach (var c in _gpuEngines) c.Dispose();
        foreach (var c in _netRx) c.Dispose();
        foreach (var c in _netTx) c.Dispose();
    }

    // ----- GlobalMemoryStatusEx (instant, no counter warmup) -----
    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;       // % of physical memory in use (0..100)
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    private static void ReadMemoryStatus(out MEMORYSTATUSEX mem)
    {
        mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
    }
}
