using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Viewer;

public sealed class MonitorViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    // ---- System Overview & Motherboard ----
    private string _motherboard = "Loading...";
    public string Motherboard { get => _motherboard; set { _motherboard = value; OnPropertyChanged(); } }

    private string _bios = "Loading...";
    public string Bios { get => _bios; set { _bios = value; OnPropertyChanged(); } }

    private string _uptime = "0m";
    public string Uptime { get => _uptime; set { _uptime = value; OnPropertyChanged(); } }

    // ---- CPU ----
    private string _cpuName = "Loading...";
    public string CpuName { get => _cpuName; set { _cpuName = value; OnPropertyChanged(); } }

    private string _cpuDetails = "";
    public string CpuDetails { get => _cpuDetails; set { _cpuDetails = value; OnPropertyChanged(); } }

    private double _cpuPercent;
    public double CpuPercent { get => _cpuPercent; set { _cpuPercent = value; OnPropertyChanged(); } }

    private string _cpuPercentText = "0%";
    public string CpuPercentText { get => _cpuPercentText; set { _cpuPercentText = value; OnPropertyChanged(); } }

    public ObservableCollection<CoreUsageItem> Cores { get; } = new();

    // ---- RAM ----
    private double _ramPercent;
    public double RamPercent { get => _ramPercent; set { _ramPercent = value; OnPropertyChanged(); } }

    private string _ramText = "0 / 0 GB (0%)";
    public string RamText { get => _ramText; set { _ramText = value; OnPropertyChanged(); } }

    public ObservableCollection<MemoryModuleItem> MemoryModules { get; } = new();

    // ---- Storage ----
    private string _diskReadRate = "0.00 MB/s";
    public string DiskReadRate { get => _diskReadRate; set { _diskReadRate = value; OnPropertyChanged(); } }

    private string _diskWriteRate = "0.00 MB/s";
    public string DiskWriteRate { get => _diskWriteRate; set { _diskWriteRate = value; OnPropertyChanged(); } }

    public ObservableCollection<PhysicalDiskItem> PhysicalDisks { get; } = new();
    public ObservableCollection<VolumeUsageItem> Volumes { get; } = new();

    // ---- Network ----
    private string _netSentRate = "0.00 Mbps";
    public string NetSentRate { get => _netSentRate; set { _netSentRate = value; OnPropertyChanged(); } }

    private string _netRecvRate = "0.00 Mbps";
    public string NetRecvRate { get => _netRecvRate; set { _netRecvRate = value; OnPropertyChanged(); } }

    public ObservableCollection<NetworkAdapterItem> NetworkAdapters { get; } = new();

    // ---- GPU ----
    public ObservableCollection<GpuItem> Gpus { get; } = new();

    // ---- Battery ----
    private string _batteryInfo = "Desktop / AC Connected";
    public string BatteryInfo { get => _batteryInfo; set { _batteryInfo = value; OnPropertyChanged(); } }

    private bool _hasBattery;
    public bool HasBattery { get => _hasBattery; set { _hasBattery = value; OnPropertyChanged(); } }

    // ---- Processes ----
    public ObservableCollection<ProcessItem> Processes { get; } = new();

    public void UpdateInventory(SystemInventoryMessage inv)
    {
        Motherboard = $"{inv.MotherboardManufacturer} {inv.MotherboardProduct}".Trim();
        Bios = inv.BiosVersion;

        CpuName = inv.Cpu.Name;
        CpuDetails = $"{inv.Cpu.Architecture} | {inv.Cpu.PhysicalCores} Cores / {inv.Cpu.LogicalCores} Threads | Clock: {inv.Cpu.MaxClockSpeedMhz} MHz | L2: {inv.Cpu.L2CacheSize} | L3: {inv.Cpu.L3CacheSize} | Socket: {inv.Cpu.Socket}";

        // Memory Modules
        MemoryModules.Clear();
        foreach (var mod in inv.MemoryModules)
        {
            var capGb = mod.CapacityBytes > 0 ? $"{mod.CapacityBytes / 1_073_741_824.0:F1} GB" : "Unknown";
            var speedStr = mod.SpeedMhz > 0 ? $"{mod.SpeedMhz} MHz" : "";
            MemoryModules.Add(new MemoryModuleItem(
                Slot: string.IsNullOrWhiteSpace(mod.DeviceLocator) ? mod.BankLabel : mod.DeviceLocator,
                Capacity: capGb,
                TypeAndSpeed: $"{mod.MemoryType} {speedStr}".Trim(),
                Manufacturer: mod.Manufacturer,
                PartNumber: mod.PartNumber
            ));
        }

        // Physical Disks
        PhysicalDisks.Clear();
        foreach (var disk in inv.PhysicalDisks)
        {
            var sizeGb = disk.SizeBytes > 0 ? $"{disk.SizeBytes / 1_073_741_824.0:F1} GB" : "Unknown";
            PhysicalDisks.Add(new PhysicalDiskItem(
                Model: disk.Model,
                Type: disk.MediaType,
                Interface: disk.InterfaceType,
                Size: sizeGb,
                Serial: disk.SerialNumber
            ));
        }

        // Network Adapters
        NetworkAdapters.Clear();
        foreach (var nic in inv.NetworkAdapters)
        {
            var speedMb = nic.SpeedBps > 0 ? $"{nic.SpeedBps / 1_000_000} Mbps" : "N/A";
            var ipStr = nic.IPv4Addresses.Length > 0 ? string.Join(", ", nic.IPv4Addresses) : "No IPv4";
            var gwStr = nic.Gateways.Length > 0 ? string.Join(", ", nic.Gateways) : "None";
            var dnsStr = nic.DnsServers.Length > 0 ? string.Join(", ", nic.DnsServers) : "None";

            NetworkAdapters.Add(new NetworkAdapterItem(
                Name: nic.Name,
                Description: nic.Description,
                Type: nic.AdapterType,
                Mac: nic.MacAddress,
                Speed: speedMb,
                IPv4: ipStr,
                Gateways: gwStr,
                Dns: dnsStr,
                Dhcp: nic.IsDhcpEnabled ? "Enabled" : "Manual"
            ));
        }

        // GPUs
        Gpus.Clear();
        foreach (var gpu in inv.Gpus)
        {
            var vramMb = gpu.VideoMemoryBytes > 0 ? $"{gpu.VideoMemoryBytes / 1_048_576} MB" : "Shared / System";
            var hzStr = gpu.RefreshRateHz > 0 ? $"{gpu.RefreshRateHz} Hz" : "";
            Gpus.Add(new GpuItem(
                Name: gpu.Name,
                DriverVersion: gpu.DriverVersion,
                Vram: vramMb,
                Resolution: $"{gpu.CurrentResolution} {hzStr}".Trim()
            ));
        }

        // Battery
        if (inv.Battery != null && inv.Battery.Value.EstimatedChargePercent > 0)
        {
            HasBattery = true;
            var b = inv.Battery.Value;
            var timeStr = b.EstimatedRunTimeMinutes > 0 ? $" ({b.EstimatedRunTimeMinutes} min remaining)" : "";
            BatteryInfo = $"{b.EstimatedChargePercent}% - {b.BatteryStatus}{timeStr}";
        }
        else
        {
            HasBattery = false;
            BatteryInfo = "Desktop / AC Powered";
        }
    }

    public void UpdateStats(SystemStatsMessage stats)
    {
        // CPU Total & Per Core
        CpuPercent = stats.CpuPercent;
        CpuPercentText = $"{stats.CpuPercent:F1}%";

        if (stats.PerCoreCpu != null && stats.PerCoreCpu.Length > 0)
        {
            while (Cores.Count < stats.PerCoreCpu.Length)
                Cores.Add(new CoreUsageItem(Cores.Count, 0));
            while (Cores.Count > stats.PerCoreCpu.Length)
                Cores.RemoveAt(Cores.Count - 1);

            for (int i = 0; i < stats.PerCoreCpu.Length; i++)
            {
                Cores[i].UsagePercent = stats.PerCoreCpu[i];
            }
        }

        // RAM
        long totalRam = stats.TotalRamBytes > 0 ? stats.TotalRamBytes : stats.RamTotalMb * 1_048_576L;
        long usedRam = stats.UsedRamBytes > 0 ? stats.UsedRamBytes : stats.RamUsedMb * 1_048_576L;
        double ramPct = totalRam > 0 ? (double)usedRam / totalRam * 100.0 : 0;
        RamPercent = ramPct;
        double usedGb = usedRam / 1_073_741_824.0;
        double totalGb = totalRam / 1_073_741_824.0;
        RamText = $"{usedGb:F1} GB / {totalGb:F1} GB ({ramPct:F1}%)";

        // Disk IO
        DiskReadRate = $"{stats.DiskReadMbps:F2} MB/s";
        DiskWriteRate = $"{stats.DiskWriteMbps:F2} MB/s";

        // Volumes
        if (stats.Volumes != null && stats.Volumes.Length > 0)
        {
            // Sync volume items
            while (Volumes.Count < stats.Volumes.Length)
                Volumes.Add(new VolumeUsageItem("", 0, 0, 0, 0));
            while (Volumes.Count > stats.Volumes.Length)
                Volumes.RemoveAt(Volumes.Count - 1);

            for (int i = 0; i < stats.Volumes.Length; i++)
            {
                var v = stats.Volumes[i];
                Volumes[i].DriveLetter = v.DriveLetter;
                Volumes[i].TotalBytes = v.TotalBytes;
                Volumes[i].UsedBytes = v.UsedBytes;
                Volumes[i].FreeBytes = v.FreeBytes;
                Volumes[i].UsedPercent = v.UsedPercent;
            }
        }

        // Network Rates
        NetSentRate = $"{stats.NetSentMbps:F2} Mbps";
        NetRecvRate = $"{stats.NetRecvMbps:F2} Mbps";

        // Top Processes
        Processes.Clear();
        if (stats.TopProcesses != null)
        {
            foreach (var p in stats.TopProcesses)
            {
                Processes.Add(new ProcessItem(p.Pid, p.Name, p.CpuPercent, p.RamMb));
            }
        }

        // Uptime
        if (stats.UptimeSeconds > 0)
        {
            var ts = TimeSpan.FromSeconds(stats.UptimeSeconds);
            Uptime = ts.TotalDays >= 1
                ? $"{(int)ts.TotalDays}d {ts.Hours}h {ts.Minutes}m {ts.Seconds}s"
                : $"{ts.Hours}h {ts.Minutes}m {ts.Seconds}s";
        }
    }
}

public sealed class CoreUsageItem : INotifyPropertyChanged
{
    public int CoreIndex { get; }
    public string CoreLabel => $"Core {CoreIndex}";

    private float _usagePercent;
    public float UsagePercent
    {
        get => _usagePercent;
        set { _usagePercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsageText)); }
    }

    public string UsageText => $"{UsagePercent:F0}%";

    public CoreUsageItem(int coreIndex, float usage)
    {
        CoreIndex = coreIndex;
        UsagePercent = usage;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class VolumeUsageItem : INotifyPropertyChanged
{
    private string _driveLetter = "";
    public string DriveLetter { get => _driveLetter; set { _driveLetter = value; OnPropertyChanged(); } }

    private long _totalBytes;
    public long TotalBytes { get => _totalBytes; set { _totalBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsageSummary)); } }

    private long _usedBytes;
    public long UsedBytes { get => _usedBytes; set { _usedBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsageSummary)); } }

    private long _freeBytes;
    public long FreeBytes { get => _freeBytes; set { _freeBytes = value; OnPropertyChanged(); OnPropertyChanged(nameof(UsageSummary)); } }

    private double _usedPercent;
    public double UsedPercent { get => _usedPercent; set { _usedPercent = value; OnPropertyChanged(); OnPropertyChanged(nameof(PercentText)); } }

    public string PercentText => $"{UsedPercent:F1}%";
    public string UsageSummary => $"{UsedBytes / 1_073_741_824.0:F1} GB used / {TotalBytes / 1_073_741_824.0:F1} GB ({FreeBytes / 1_073_741_824.0:F1} GB free)";

    public VolumeUsageItem(string drive, long total, long free, long used, double pct)
    {
        _driveLetter = drive;
        _totalBytes = total;
        _freeBytes = free;
        _usedBytes = used;
        _usedPercent = pct;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public record struct MemoryModuleItem(string Slot, string Capacity, string TypeAndSpeed, string Manufacturer, string PartNumber);
public record struct PhysicalDiskItem(string Model, string Type, string Interface, string Size, string Serial);
public record struct NetworkAdapterItem(string Name, string Description, string Type, string Mac, string Speed, string IPv4, string Gateways, string Dns, string Dhcp);
public record struct GpuItem(string Name, string DriverVersion, string Vram, string Resolution);
public record struct ProcessItem(int Pid, string Name, double CpuPercent, long RamMb);
