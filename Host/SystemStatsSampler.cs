using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Host;

/// <summary>
/// High-efficiency system stats sampler. Reuses performance counters and computes
/// differential metrics (CPU % per process, network rates per adapter) without memory or handle leaks.
/// </summary>
public sealed class SystemStatsSampler : IDisposable
{
    private readonly PerformanceCounter? _cpuTotalCounter;
    private readonly PerformanceCounter[] _cpuCoreCounters;
    private readonly PerformanceCounter? _diskReadCounter;
    private readonly PerformanceCounter? _diskWriteCounter;

    // Process CPU tracking state: PID -> (TotalProcessorTime, Timestamp)
    private readonly Dictionary<int, (TimeSpan CpuTime, DateTime Timestamp)> _prevProcessTimes = new();

    // Network bandwidth tracking state: Interface Id -> (BytesReceived, BytesSent, Timestamp)
    private readonly Dictionary<string, (long RecvBytes, long SentBytes, DateTime Timestamp)> _prevNetStats = new();

    private bool _disposed;

    public SystemStatsSampler()
    {
        try
        {
            _cpuTotalCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total");
            _cpuTotalCounter.NextValue(); // prime reading
        }
        catch { }

        var coreCounters = new List<PerformanceCounter>();
        for (int i = 0; i < Environment.ProcessorCount; i++)
        {
            try
            {
                var core = new PerformanceCounter("Processor", "% Processor Time", i.ToString());
                core.NextValue(); // prime reading
                coreCounters.Add(core);
            }
            catch { }
        }
        _cpuCoreCounters = coreCounters.ToArray();

        try
        {
            _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskReadCounter.NextValue();
            _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            _diskWriteCounter.NextValue();
        }
        catch { }
    }

    public SystemStatsMessage Sample()
    {
        // 1. Total & Per-Core CPU
        double totalCpu = 0;
        try { totalCpu = _cpuTotalCounter?.NextValue() ?? 0; }
        catch { }

        var perCoreCpu = new float[_cpuCoreCounters.Length];
        for (int i = 0; i < _cpuCoreCounters.Length; i++)
        {
            try { perCoreCpu[i] = _cpuCoreCounters[i].NextValue(); }
            catch { perCoreCpu[i] = 0; }
        }

        // 2. Memory via GlobalMemoryStatusEx
        var (totalRam, availRam, usedRam) = SampleMemory();

        // 3. Disk I/O
        double diskReadBytes = 0;
        double diskWriteBytes = 0;
        try
        {
            diskReadBytes = _diskReadCounter?.NextValue() ?? 0;
            diskWriteBytes = _diskWriteCounter?.NextValue() ?? 0;
        }
        catch { }

        var diskIoList = new List<DiskIoData>
        {
            new("Total", diskReadBytes, diskWriteBytes)
        };

        // 4. Volume usages
        var volumeUsages = new List<VolumeUsageData>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!d.IsReady) continue;
                long total = d.TotalSize;
                long free = d.AvailableFreeSpace;
                long used = total - free;
                double pct = total > 0 ? (double)used / total * 100.0 : 0;
                volumeUsages.Add(new VolumeUsageData(d.Name, total, free, used, Math.Round(pct, 1)));
            }
        }
        catch { }

        // 5. Network throughput per adapter (delta over time)
        var now = DateTime.UtcNow;
        var netAdapters = new List<NetworkTrafficData>();
        double totalRecvBps = 0;
        double totalSentBps = 0;

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                try
                {
                    var stats = nic.GetIPv4Statistics();
                    long currentRecv = stats.BytesReceived;
                    long currentSent = stats.BytesSent;

                    if (_prevNetStats.TryGetValue(nic.Id, out var prev))
                    {
                        var elapsed = (now - prev.Timestamp).TotalSeconds;
                        if (elapsed > 0.1)
                        {
                            double recvRate = Math.Max(0, (currentRecv - prev.RecvBytes) / elapsed);
                            double sentRate = Math.Max(0, (currentSent - prev.SentBytes) / elapsed);

                            netAdapters.Add(new NetworkTrafficData(nic.Name, recvRate, sentRate));
                            totalRecvBps += recvRate;
                            totalSentBps += sentRate;
                        }
                    }

                    _prevNetStats[nic.Id] = (currentRecv, currentSent, now);
                }
                catch { }
            }
        }
        catch { }

        // 6. Top processes with calculated CPU %
        var topProcesses = SampleProcesses(now);

        // 7. System Uptime
        long uptimeSec = Environment.TickCount64 / 1000;

        return new SystemStatsMessage(
            CpuPercent: Math.Round(totalCpu, 1),
            PerCoreCpu: perCoreCpu,
            RamUsedMb: usedRam / 1_048_576L,
            RamTotalMb: totalRam / 1_048_576L,
            TotalRamBytes: totalRam,
            UsedRamBytes: usedRam,
            AvailableRamBytes: availRam,
            DiskReadMbps: Math.Round(diskReadBytes / 1_048_576.0, 2),
            DiskWriteMbps: Math.Round(diskWriteBytes / 1_048_576.0, 2),
            NetSentMbps: Math.Round(totalSentBps * 8 / 1_000_000.0, 2),
            NetRecvMbps: Math.Round(totalRecvBps * 8 / 1_000_000.0, 2),
            Volumes: volumeUsages.ToArray(),
            Disks: diskIoList.ToArray(),
            NetworkAdapters: netAdapters.ToArray(),
            TopProcesses: topProcesses,
            UptimeSeconds: uptimeSec
        );
    }

    private ProcessInfoData[] SampleProcesses(DateTime now)
    {
        var procList = new List<ProcessInfoData>();
        var currentPids = new HashSet<int>();
        var logicalCores = Environment.ProcessorCount;

        try
        {
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                int pid = p.Id;
                if (pid == 0) continue; // skip System Idle Process
                currentPids.Add(pid);

                string name = p.ProcessName;
                long ramMb = 0;
                double cpuPct = 0;

                try
                {
                    ramMb = p.WorkingSet64 / 1_048_576L;
                    var cpuTime = p.TotalProcessorTime;

                    if (_prevProcessTimes.TryGetValue(pid, out var prev))
                    {
                        var elapsedSec = (now - prev.Timestamp).TotalSeconds;
                        if (elapsedSec > 0.1)
                        {
                            var deltaCpuMs = (cpuTime - prev.CpuTime).TotalMilliseconds;
                            cpuPct = deltaCpuMs / (elapsedSec * 1000.0 * logicalCores) * 100.0;
                            if (cpuPct < 0) cpuPct = 0;
                            if (cpuPct > 100) cpuPct = 100;
                        }
                    }

                    _prevProcessTimes[pid] = (cpuTime, now);
                }
                catch
                {
                    // Access denied or process exited during reading
                }

                procList.Add(new ProcessInfoData(pid, name, Math.Round(cpuPct, 1), ramMb));
            }
        }
        catch { }

        // Clean up vanished PIDs from history cache
        var stalePids = _prevProcessTimes.Keys.Where(k => !currentPids.Contains(k)).ToList();
        foreach (var pid in stalePids)
            _prevProcessTimes.Remove(pid);

        // Sort: primary by CPU desc, secondary by RAM desc; take top 25
        return procList
            .OrderByDescending(p => p.CpuPercent)
            .ThenByDescending(p => p.RamMb)
            .Take(25)
            .ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx lpBuffer);

    private static (long Total, long Available, long Used) SampleMemory()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (GlobalMemoryStatusEx(ref status))
        {
            long total = (long)status.ullTotalPhys;
            long avail = (long)status.ullAvailPhys;
            return (total, avail, total - avail);
        }
        return (0, 0, 0);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cpuTotalCounter?.Dispose(); } catch { }
        foreach (var c in _cpuCoreCounters)
        {
            try { c.Dispose(); } catch { }
        }
        try { _diskReadCounter?.Dispose(); } catch { }
        try { _diskWriteCounter?.Dispose(); } catch { }
    }
}
