using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace AgentManager.Core.Monitoring;

/// <summary>One sampling instant of host resource usage. Percent values are 0..100, -1 = unknown.
/// Network rates are bytes/sec (>=0). <see cref="GpuAvailable"/>/<see cref="VramAvailable"/> are false
/// when the corresponding GPU counter / adapter info could not be read.</summary>
public sealed record ResourceSnapshot(
    double CpuPercent,
    double GpuPercent,
    double MemoryPercent,
    ulong MemoryUsedBytes,
    ulong MemoryTotalBytes,
    double NetRecvBytesPerSec,
    double NetSentBytesPerSec,
    bool GpuAvailable,
    ulong VramUsedBytes,
    ulong VramTotalBytes,
    bool VramAvailable)
{
    public static ResourceSnapshot Empty { get; } = new(-1, -1, 0, 0, 0, 0, 0, false, 0, 0, false);
}

/// <summary>Headless host resource monitor: samples CPU / GPU / GPU VRAM / RAM / Ethernet once per second
/// on a background thread and raises <see cref="Sampled"/> with a fresh <see cref="ResourceSnapshot"/>.
/// Holds no UI types — callers format snapshots for display. Lives in Core (alongside the Win32
/// <c>ConPtyHost</c>) so the headless smoke harness can exercise it; it is Windows-only (Win32
/// <c>GlobalMemoryStatusEx</c>, the registry, and GPU Engine / GPU Adapter Memory performance counters).
///
/// VRAM total is read once from the registry (<c>HardwareInformation.qwMemorySize</c>, the largest adapter
/// = the dGPU; a QWORD so it does not wrap at 4 GiB like <c>Win32_VideoController.AdapterRAM</c>), and
/// VRAM used is the largest single <c>GPU Adapter Memory\Dedicated Usage</c> instance (the primary GPU,
/// which dominates usage) — both target the same GPU, so no LUID matching is needed and it matches
/// Task Manager's per-GPU "Dedicated GPU memory" (verified equal to nvidia-smi).
///
/// Cost: a single ~1 Hz threadpool tick doing one <c>% Processor Time</c> read, a handful of GPU engine
/// counter reads (summed for the first physical GPU), a few GPU Adapter Memory reads, a few network-adapter
/// reads, and one memory P/Invoke — well under 1% CPU and a few KB of state. Counters are created once
/// (seeded) and reused; each <c>NextValue</c> is O(1). Everything is guarded so a missing counter/category/
/// adapter degrades to "—" instead of throwing.</summary>
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
    // VRAM: total (from registry, once) + Dedicated Usage counters for each phys_0 adapter.
    private readonly List<PerformanceCounter> _vramCounters = new();
    private ulong _vramTotalBytes;
    private bool _vramChecked;
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

            var vramUsedRaw = -1.0;
            foreach (var c in _vramCounters) { var v = c.NextValue(); if (v > vramUsedRaw) vramUsedRaw = v; } // largest single adapter = the primary GPU
            var vramAvailable = vramUsedRaw >= 0 && _vramTotalBytes > 0;
            var vramUsed = vramAvailable ? (ulong)Math.Max(0, vramUsedRaw) : 0;

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
                GpuAvailable: gpu >= 0,
                VramUsedBytes: vramUsed,
                VramTotalBytes: _vramTotalBytes,
                VramAvailable: vramAvailable);
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

        EnsureVram();

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

    /// <summary>Resolve VRAM once: total from the registry (largest adapter's qwMemorySize), used from the
    /// largest <c>GPU Adapter Memory\Dedicated Usage</c> <c>phys_0</c> instance (the primary GPU).</summary>
    private void EnsureVram()
    {
        if (_vramChecked) return;
        _vramChecked = true;
        _vramTotalBytes = ReadMaxVramBytes();
        if (_vramTotalBytes == 0) return; // no adapter reported VRAM → leave counters empty → unavailable
        try
        {
            var cat = new PerformanceCounterCategory("GPU Adapter Memory");
            foreach (var inst in cat.GetInstanceNames())
            {
                if (!inst.Contains("phys_0", StringComparison.OrdinalIgnoreCase)) continue;
                var c = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", inst, readOnly: true);
                c.NextValue(); // seed
                _vramCounters.Add(c);
            }
        }
        catch { /* no GPU Adapter Memory category → used stays 0, shown as "—" since unavailable */ }
        if (_vramCounters.Count == 0) _vramTotalBytes = 0;
    }

    /// <summary>Largest dedicated VRAM (bytes) across Display-adapter registry subkeys. <c>qwMemorySize</c>
    /// is a QWORD so it does not wrap at 4 GiB (unlike Win32_VideoController.AdapterRAM). Read via advapi32
    /// P/Invoke (no registry NuGet dependency); accepts REG_QWORD / REG_BINARY(8B) / REG_DWORD.</summary>
    private static ulong ReadMaxVramBytes()
    {
        const string displayClass = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        if (RegOpenKeyEx(HKEY_LOCAL_MACHINE, displayClass, 0, KEY_READ, out IntPtr cls) != 0) return 0;
        ulong best = 0;
        try
        {
            for (uint i = 0; ; i++)
            {
                var name = new StringBuilder(256);
                uint len = 256;
                if (RegEnumKeyEx(cls, i, name, ref len, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero) != 0) break;
                if (RegOpenKeyEx(cls, name.ToString(), 0, KEY_READ, out IntPtr sk) == 0)
                {
                    try { var b = ReadRegistryQword(sk, "HardwareInformation.qwMemorySize"); if (b > best) best = b; }
                    finally { RegCloseKey(sk); }
                }
            }
        }
        finally { RegCloseKey(cls); }
        return best;
    }

    private static ulong ReadRegistryQword(IntPtr hKey, string valueName)
    {
        var buf = new byte[16];
        uint size = 16;
        if (RegQueryValueEx(hKey, valueName, IntPtr.Zero, out uint type, buf, ref size) != 0 || size < 8) return 0;
        return type == 11 || type == 3 ? BitConverter.ToUInt64(buf, 0)   // REG_QWORD or REG_BINARY
            : type == 4 ? BitConverter.ToUInt32(buf, 0)                  // REG_DWORD
            : 0;
    }

    private static readonly IntPtr HKEY_LOCAL_MACHINE = new(0x80000002);
    private const int KEY_READ = 0x20019;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegOpenKeyEx(IntPtr hKey, string subKey, uint ulOptions, int samDesired, out IntPtr phkResult);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegEnumKeyEx(IntPtr hKey, uint index, StringBuilder name, ref uint nameLen, IntPtr reserved, IntPtr lpClass, IntPtr lpcchClass, IntPtr lpftLastWriteTime);
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegQueryValueExW")]
    private static extern int RegQueryValueEx(IntPtr hKey, string valueName, IntPtr reserved, out uint type, byte[] data, ref uint dataSize);
    [DllImport("advapi32.dll", EntryPoint = "RegCloseKey")]
    private static extern int RegCloseKey(IntPtr hKey);

    public void Dispose()
    {
        _timer.Dispose();
        _cpu?.Dispose();
        if (_gpuEngines is not null) foreach (var c in _gpuEngines) c.Dispose();
        foreach (var c in _vramCounters) c.Dispose();
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
