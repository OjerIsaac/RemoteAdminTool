using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using RemoteAdminTool.Shared;

namespace RemoteAdminTool.Host;

/// <summary>
/// Safely extracts full hardware topology and inventory using WMI, Registry, and .NET APIs.
/// Every section is wrapped in individual try/catch blocks to ensure robust execution
/// across heterogeneous Windows versions without ever failing the session.
/// </summary>
public static class SystemInventoryCollector
{
    public static SystemInventoryMessage Collect()
    {
        var (mbMfr, mbProd) = GetMotherboardInfo();
        var biosVer = GetBiosVersion();
        var cpu = GetCpuInfo();
        var memoryModules = GetMemoryModules();
        var physicalDisks = GetPhysicalDisks();
        var volumes = GetVolumes();
        var networkAdapters = GetNetworkAdapters();
        var gpus = GetGpus();
        var battery = GetBatteryInfo();

        return new SystemInventoryMessage(
            MotherboardManufacturer: mbMfr,
            MotherboardProduct: mbProd,
            BiosVersion: biosVer,
            Cpu: cpu,
            MemoryModules: memoryModules,
            PhysicalDisks: physicalDisks,
            Volumes: volumes,
            NetworkAdapters: networkAdapters,
            Gpus: gpus,
            Battery: battery
        );
    }

    private static (string Manufacturer, string Product) GetMotherboardInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (ManagementObject obj in searcher.Get())
            {
                var mfr = obj["Manufacturer"]?.ToString()?.Trim() ?? "Unknown";
                var prod = obj["Product"]?.ToString()?.Trim() ?? "Unknown";
                return (mfr, prod);
            }
        }
        catch { }

        // Fallback: Registry
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (key != null)
            {
                var mfr = key.GetValue("BaseBoardManufacturer")?.ToString()?.Trim() ?? "Unknown";
                var prod = key.GetValue("BaseBoardProduct")?.ToString()?.Trim() ?? "Unknown";
                return (mfr, prod);
            }
        }
        catch { }

        return ("Unknown", "Unknown");
    }

    private static string GetBiosVersion()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            foreach (ManagementObject obj in searcher.Get())
            {
                var ver = obj["SMBIOSBIOSVersion"]?.ToString()?.Trim() ?? "";
                return string.IsNullOrWhiteSpace(ver) ? "Unknown" : ver;
            }
        }
        catch { }

        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            if (key != null)
            {
                var ver = key.GetValue("BIOSVersion")?.ToString()?.Trim();
                if (!string.IsNullOrEmpty(ver)) return ver;
            }
        }
        catch { }

        return "Unknown";
    }

    private static CpuInfoData GetCpuInfo()
    {
        string name = "Unknown";
        string arch = RuntimeInformation.ProcessArchitecture.ToString();
        int physCores = Environment.ProcessorCount;
        int logCores = Environment.ProcessorCount;
        int maxClockMhz = 0;
        string l2 = "Unknown";
        string l3 = "Unknown";
        string socket = "Unknown";

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, L2CacheSize, L3CacheSize, SocketDesignation FROM Win32_Processor");
            foreach (ManagementObject obj in searcher.Get())
            {
                name = obj["Name"]?.ToString()?.Trim() ?? name;
                if (int.TryParse(obj["NumberOfCores"]?.ToString(), out var pc)) physCores = pc;
                if (int.TryParse(obj["NumberOfLogicalProcessors"]?.ToString(), out var lc)) logCores = lc;
                if (int.TryParse(obj["MaxClockSpeed"]?.ToString(), out var mhz)) maxClockMhz = mhz;
                var l2Val = obj["L2CacheSize"]?.ToString();
                if (!string.IsNullOrEmpty(l2Val)) l2 = $"{l2Val} KB";
                var l3Val = obj["L3CacheSize"]?.ToString();
                if (!string.IsNullOrEmpty(l3Val)) l3 = $"{l3Val} KB";
                socket = obj["SocketDesignation"]?.ToString()?.Trim() ?? socket;
                break;
            }
        }
        catch { }

        // Fallback: Registry for CPU name if still unknown
        if (name == "Unknown")
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
                name = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? "Unknown";
            }
            catch { }
        }

        return new CpuInfoData(
            Name: name,
            Architecture: arch,
            PhysicalCores: physCores,
            LogicalCores: logCores,
            MaxClockSpeedMhz: maxClockMhz,
            L2CacheSize: l2,
            L3CacheSize: l3,
            Socket: socket
        );
    }

    private static MemoryModuleData[] GetMemoryModules()
    {
        var modules = new List<MemoryModuleData>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT BankLabel, DeviceLocator, Capacity, Speed, Manufacturer, PartNumber, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            foreach (ManagementObject obj in searcher.Get())
            {
                var bank = obj["BankLabel"]?.ToString()?.Trim() ?? "";
                var locator = obj["DeviceLocator"]?.ToString()?.Trim() ?? "DIMM";
                long.TryParse(obj["Capacity"]?.ToString(), out var cap);
                int.TryParse(obj["Speed"]?.ToString(), out var speed);
                var mfr = obj["Manufacturer"]?.ToString()?.Trim() ?? "Unknown";
                var part = obj["PartNumber"]?.ToString()?.Trim() ?? "Unknown";
                int.TryParse(obj["SMBIOSMemoryType"]?.ToString(), out var typeInt);

                var memType = typeInt switch
                {
                    20 => "DDR",
                    21 => "DDR2",
                    24 => "DDR3",
                    26 => "DDR4",
                    30 => "LPDDR4",
                    34 => "DDR5",
                    35 => "LPDDR5",
                    _ => typeInt > 0 ? $"Type {typeInt}" : "DDR/SDRAM"
                };

                modules.Add(new MemoryModuleData(
                    BankLabel: bank,
                    DeviceLocator: locator,
                    CapacityBytes: cap,
                    SpeedMhz: speed,
                    Manufacturer: mfr,
                    PartNumber: part,
                    MemoryType: memType
                ));
            }
        }
        catch { }

        // If WMI yielded nothing, provide total physical RAM as single module fallback
        if (modules.Count == 0)
        {
            var total = GetTotalMemoryViaKernel();
            if (total > 0)
            {
                modules.Add(new MemoryModuleData(
                    BankLabel: "Primary",
                    DeviceLocator: "System RAM",
                    CapacityBytes: total,
                    SpeedMhz: 0,
                    Manufacturer: "Standard",
                    PartNumber: "RAM",
                    MemoryType: "System Memory"
                ));
            }
        }

        return modules.ToArray();
    }

    private static PhysicalDiskData[] GetPhysicalDisks()
    {
        var disks = new List<PhysicalDiskData>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Index, Model, InterfaceType, Size, SerialNumber, MediaType FROM Win32_DiskDrive");
            foreach (ManagementObject obj in searcher.Get())
            {
                int.TryParse(obj["Index"]?.ToString(), out var idx);
                var model = obj["Model"]?.ToString()?.Trim() ?? "Unknown Disk";
                var iface = obj["InterfaceType"]?.ToString()?.Trim() ?? "Unknown";
                long.TryParse(obj["Size"]?.ToString(), out var size);
                var serial = obj["SerialNumber"]?.ToString()?.Trim() ?? "";
                var media = obj["MediaType"]?.ToString()?.Trim() ?? "";

                // Infer media type if unspecified
                var inferredType = "Drive";
                if (model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                    inferredType = "NVMe SSD";
                else if (model.Contains("SSD", StringComparison.OrdinalIgnoreCase) || media.Contains("SSD", StringComparison.OrdinalIgnoreCase))
                    inferredType = "SSD";
                else if (media.Contains("Fixed hard disk", StringComparison.OrdinalIgnoreCase))
                    inferredType = "HDD";
                else if (!string.IsNullOrEmpty(media))
                    inferredType = media;

                disks.Add(new PhysicalDiskData(
                    Index: idx,
                    Model: model,
                    InterfaceType: iface,
                    SizeBytes: size,
                    MediaType: inferredType,
                    SerialNumber: serial
                ));
            }
        }
        catch { }

        // Fallback: Use DriveInfo if WMI yielded nothing
        if (disks.Count == 0)
        {
            try
            {
                int idx = 0;
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (!d.IsReady) continue;
                    disks.Add(new PhysicalDiskData(
                        Index: idx++,
                        Model: $"{d.Name} ({d.DriveFormat} {d.VolumeLabel})".Trim(),
                        InterfaceType: d.DriveType.ToString(),
                        SizeBytes: d.TotalSize,
                        MediaType: "Drive",
                        SerialNumber: ""
                    ));
                }
            }
            catch { }
        }

        return disks.ToArray();
    }

    private static VolumeData[] GetVolumes()
    {
        var volumes = new List<VolumeData>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;

                volumes.Add(new VolumeData(
                    DriveLetter: drive.Name,
                    Label: string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                    FileSystem: drive.DriveFormat,
                    TotalBytes: drive.TotalSize,
                    FreeBytes: drive.AvailableFreeSpace,
                    DriveType: drive.DriveType.ToString()
                ));
            }
        }
        catch { }

        return volumes.ToArray();
    }

    private static NetworkAdapterData[] GetNetworkAdapters()
    {
        var adapters = new List<NetworkAdapterData>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Skip loopback and tunnel interfaces
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                    continue;

                var ipProps = nic.GetIPProperties();
                var ipv4s = ipProps.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(u => u.Address.ToString())
                    .ToArray();

                var ipv6s = ipProps.UnicastAddresses
                    .Where(u => u.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
                    .Select(u => u.Address.ToString())
                    .ToArray();

                var gateways = ipProps.GatewayAddresses
                    .Select(g => g.Address.ToString())
                    .ToArray();

                var dns = ipProps.DnsAddresses
                    .Select(d => d.ToString())
                    .ToArray();

                var mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));

                bool isDhcp = false;
                try { isDhcp = ipProps.GetIPv4Properties()?.IsDhcpEnabled ?? false; }
                catch { }

                adapters.Add(new NetworkAdapterData(
                    Id: nic.Id,
                    Name: nic.Name,
                    Description: nic.Description,
                    MacAddress: mac,
                    AdapterType: nic.NetworkInterfaceType.ToString(),
                    SpeedBps: nic.Speed,
                    IPv4Addresses: ipv4s,
                    IPv6Addresses: ipv6s,
                    Gateways: gateways,
                    DnsServers: dns,
                    IsDhcpEnabled: isDhcp
                ));
            }
        }
        catch { }

        return adapters.ToArray();
    }

    private static GpuData[] GetGpus()
    {
        var gpus = new List<GpuData>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion, AdapterRAM, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString()?.Trim() ?? "Video Controller";
                var drv = obj["DriverVersion"]?.ToString()?.Trim() ?? "Unknown";
                long.TryParse(obj["AdapterRAM"]?.ToString(), out var ram);
                int.TryParse(obj["CurrentHorizontalResolution"]?.ToString(), out var w);
                int.TryParse(obj["CurrentVerticalResolution"]?.ToString(), out var h);
                int.TryParse(obj["CurrentRefreshRate"]?.ToString(), out var hz);

                var res = (w > 0 && h > 0) ? $"{w} x {h}" : "Not active";

                gpus.Add(new GpuData(
                    Name: name,
                    DriverVersion: drv,
                    VideoMemoryBytes: ram,
                    CurrentResolution: res,
                    RefreshRateHz: hz
                ));
            }
        }
        catch { }

        // Fallback: Registry for Display Adapter
        if (gpus.Count == 0)
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0000");
                var desc = key?.GetValue("DriverDesc")?.ToString();
                var drvVer = key?.GetValue("DriverVersion")?.ToString() ?? "Unknown";
                if (!string.IsNullOrEmpty(desc))
                {
                    gpus.Add(new GpuData(desc, drvVer, 0, "Active Display", 60));
                }
            }
            catch { }
        }

        return gpus.ToArray();
    }

    private static BatteryData? GetBatteryInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT EstimatedChargeRemaining, BatteryStatus, EstimatedRunTime FROM Win32_Battery");
            foreach (ManagementObject obj in searcher.Get())
            {
                int.TryParse(obj["EstimatedChargeRemaining"]?.ToString(), out var charge);
                int.TryParse(obj["BatteryStatus"]?.ToString(), out var statusInt);
                int.TryParse(obj["EstimatedRunTime"]?.ToString(), out var runTime);

                var statusStr = statusInt switch
                {
                    1 => "Discharging",
                    2 => "AC Connected (Charging)",
                    3 => "Fully Charged",
                    4 => "Low",
                    5 => "Critical",
                    6 => "Charging",
                    7 => "Charging and High",
                    8 => "Charging and Low",
                    9 => "Charging and Critical",
                    _ => "AC Connected"
                };

                return new BatteryData(
                    EstimatedChargePercent: charge,
                    BatteryStatus: statusStr,
                    EstimatedRunTimeMinutes: runTime > 0 && runTime < 71582788 ? runTime : 0
                );
            }
        }
        catch { }

        return null;
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

    private static long GetTotalMemoryViaKernel()
    {
        var status = new MemoryStatusEx { dwLength = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.ullTotalPhys : 0;
    }
}
