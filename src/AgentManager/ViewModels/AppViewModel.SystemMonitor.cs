using System.Windows;
using AgentManager.Core.Monitoring;

namespace AgentManager.ViewModels;

public sealed partial class AppViewModel
{
    // ----- system resource monitor (titlebar upper-right strip) -----
    // Samples run on a background thread (see ResourceMonitor); results are marshalled to the UI thread
    // here and formatted into compact mono labels. Backing fields + Set() so unchanged values (very common
    // at 1 Hz) skip PropertyChanged, avoiding needless rebinds.
    private readonly ResourceMonitor _resourceMonitor = new();

    private string _cpuLabel = "—";
    public string CpuLabel { get => _cpuLabel; private set => Set(ref _cpuLabel, value); }

    private string _gpuLabel = "—";
    public string GpuLabel { get => _gpuLabel; private set => Set(ref _gpuLabel, value); }

    private string _memLabel = "—";
    public string MemLabel { get => _memLabel; private set => Set(ref _memLabel, value); }

    private string _netLabel = "";
    public string NetLabel { get => _netLabel; private set => Set(ref _netLabel, value); }

    /// <summary>Subscribes to background samples (marshalled onto the UI dispatcher) and starts the 1 Hz timer.</summary>
    private void StartResourceMonitor()
    {
        _resourceMonitor.Sampled += snap =>
            Application.Current.Dispatcher.BeginInvoke(new Action<ResourceSnapshot>(OnResourceSampled), snap);
        _resourceMonitor.Start();
    }

    private void OnResourceSampled(ResourceSnapshot s)
    {
        CpuLabel = s.CpuPercent < 0 ? "—" : $"{s.CpuPercent:F0}%";
        GpuLabel = s.GpuAvailable ? $"{s.GpuPercent:F0}%" : "—";
        MemLabel = $"{BytesToGiB(s.MemoryUsedBytes):0.0}/{BytesToGiB(s.MemoryTotalBytes):0.0}G";
        NetLabel = $"↑{NetRate(s.NetSentBytesPerSec)} ↓{NetRate(s.NetRecvBytesPerSec)}";
    }

    private static double BytesToGiB(ulong b) => b / 1073741824.0;

    /// <summary>Compact bytes/sec → "340k" / "1.2M" / "128".</summary>
    private static string NetRate(double bytesPerSec)
    {
        if (bytesPerSec >= 1_000_000) return (bytesPerSec / 1_000_000).ToString("0.0") + "M";
        if (bytesPerSec >= 1_000) return (bytesPerSec / 1_000).ToString("0.0") + "k";
        return Math.Max(0, bytesPerSec).ToString("0");
    }
}
