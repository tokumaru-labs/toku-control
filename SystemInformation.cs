using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TokuControl;

public record MonitorSnapshot(string Device, string Name, bool Primary, int? Width, int? Height,
    int? X, int? Y, double? Hertz, int? Scale, string Rotation);
public record MachineSnapshot(string Windows, string Build, string Cpu, uint LogicalProcessors,
    string Model, ulong? InstalledBytes, ulong? UsableBytes, ulong? AvailableBytes,
    string[] Graphics, string Drive, long? DriveTotal, long? DriveFree, TimeSpan Uptime,
    MonitorSnapshot[] Monitors, string DisplayNotice, DateTime CapturedAt);

// Snapshot queries only: no display, registry, power, or device configuration writes.
public static class SystemInformation
{
    public static MachineSnapshot Read()
    {
        const string currentVersion = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        string ReadValue(string path, string name) => Try(() =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(path, false);
            return key?.GetValue(name)?.ToString()?.Trim() ?? "";
        }, "");
        var version = Environment.OSVersion.Version;
        var product = ReadValue(currentVersion, "ProductName");
        // Windows 11 retains Windows 10 in ProductName on some installations.
        if (version.Build >= 22000) product = product.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(product)) product = "Windows " + version.Major;
        var release = ReadValue(currentVersion, "DisplayVersion");
        var revision = ReadValue(currentVersion, "UBR");
        string cpu = ReadValue(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString");
        string model = string.Join(" ", new[] { ReadValue(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemManufacturer"), ReadValue(@"HARDWARE\DESCRIPTION\System\BIOS", "SystemProductName") }
            .Where(s => s.Length > 0 && !new[] { "To Be Filled By O.E.M.", "Default string", "System Product Name", "System manufacturer" }.Contains(s, StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase));
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        bool hasMemory = GlobalMemoryStatusEx(ref memory);
        ulong? installed = GetPhysicallyInstalledSystemMemory(out var kib) ? kib * 1024 : null;
        string drive = Path.GetPathRoot(Environment.SystemDirectory) ?? "";
        long? total = null, free = null;
        try { var info = new DriveInfo(drive); total = info.TotalSize; free = info.TotalFreeSpace; } catch { }
        var graphics = new List<string>();
        for (uint i = 0; i < 128; i++)
        {
            var adapter = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref adapter, 0)) break;
            if ((adapter.Flags & 8) == 0 && !string.IsNullOrWhiteSpace(adapter.Description)) graphics.Add(adapter.Description);
        }
        var (monitors, notice) = ReadMonitors();
        return new(product + (release.Length > 0 ? " / " + release : ""),
            version.Build + (revision.Length > 0 ? "." + revision : "") + " / " + RuntimeInformation.OSArchitecture,
            cpu.Length > 0 ? cpu : "UNAVAILABLE", GetActiveProcessorCount(0xffff), model.Length > 0 ? model : "NOT PROVIDED BY FIRMWARE",
            installed, hasMemory ? memory.TotalPhysical : null, hasMemory ? memory.AvailablePhysical : null,
            graphics.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), drive, total, free,
            TimeSpan.FromMilliseconds(Environment.TickCount64), monitors, notice, DateTime.Now);
    }
    static T Try<T>(Func<T> read, T fallback) { try { return read(); } catch { return fallback; } }
    public static string Capacity(ulong? bytes) => bytes.HasValue ? (bytes.Value / 1073741824d).ToString("0.#", CultureInfo.InvariantCulture) + " GiB" : "N/A";
    public static string RefreshRate(double? hertz) => hertz is > 1 && double.IsFinite(hertz.Value) ? hertz.Value.ToString("0.##", CultureInfo.InvariantCulture) + " Hz" : "N/A";

    static (MonitorSnapshot[], string) ReadMonitors()
    {
        var desktops = new Dictionary<string, MonitorSnapshot>(StringComparer.OrdinalIgnoreCase);
        bool enumerated = EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr handle, IntPtr dc, ref NativeRect rect, IntPtr data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(handle, ref info))
            {
                int? scale = GetScaleFactorForMonitor(handle, out int factor) == 0 ? factor : null;
                desktops[info.Device] = new(info.Device, "NAME UNAVAILABLE", (info.Flags & 1) != 0,
                    info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top,
                    info.Monitor.Left, info.Monitor.Top, null, scale, "N/A");
            }
            return true;
        }, IntPtr.Zero);
        var (paths, error) = ActivePaths();
        var result = new List<MonitorSnapshot>();
        var covered = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var source = new SourceName { Header = new() { Type = 1, Size = (uint)Marshal.SizeOf<SourceName>(), Adapter = path.Source.Adapter, Id = path.Source.Id } };
            if (DisplayConfigGetDeviceInfo(ref source) != 0) continue;
            var target = new TargetName { Header = new() { Type = 2, Size = (uint)Marshal.SizeOf<TargetName>(), Adapter = path.Target.Adapter, Id = path.Target.Id } };
            string name = DisplayConfigGetDeviceInfo(ref target) == 0 && !string.IsNullOrWhiteSpace(target.Name) ? target.Name : "NAME UNAVAILABLE";
            desktops.TryGetValue(source.Device, out var desktop);
            desktop ??= new(source.Device, name, false, null, null, null, null, null, null, "N/A");
            double? hz = path.Target.Refresh.Denominator > 0 ? (double)path.Target.Refresh.Numerator / path.Target.Refresh.Denominator : null;
            result.Add(desktop with { Name = name, Hertz = hz > 1 ? hz : null, Rotation = path.Target.Rotation switch { 1 => "0 deg", 2 => "90 deg", 3 => "180 deg", 4 => "270 deg", _ => "N/A" } });
            covered.Add(source.Device);
        }
        // A mirrored source has multiple physical targets; keep every CCD target.
        result.AddRange(desktops.Where(p => !covered.Contains(p.Key)).Select(p => p.Value));
        string notice = error != 0 ? "DISPLAY DETAILS UNAVAILABLE / WIN32 " + error : !enumerated ? "DESKTOP GEOMETRY UNAVAILABLE" : "";
        return (result.OrderByDescending(m => m.Primary).ThenBy(m => m.Device, StringComparer.OrdinalIgnoreCase).ToArray(), notice);
    }
    static (DisplayPath[], int) ActivePaths()
    {
        const uint activeOnly = 2;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            int status = GetDisplayConfigBufferSizes(activeOnly, out uint paths, out uint modes);
            if (status != 0) return ([], status);
            if (paths > 4096 || modes > 16384) return ([], 8);
            var pathBuffer = new DisplayPath[Math.Max(1, paths)];
            var modeBuffer = new DisplayMode[Math.Max(1, modes)];
            status = QueryDisplayConfig(activeOnly, ref paths, pathBuffer, ref modes, modeBuffer, IntPtr.Zero);
            if (status == 122) continue; // Topology changed between size/read; bounded retry.
            return status == 0 ? (pathBuffer.Take((int)paths).ToArray(), 0) : ([], status);
        }
        return ([], 122);
    }
    internal static bool InteropSizesValid => Marshal.SizeOf<DisplayPath>() == 72 && Marshal.SizeOf<DisplayMode>() == 64
        && Marshal.SizeOf<SourceName>() == 84 && Marshal.SizeOf<TargetName>() == 420 && Marshal.SizeOf<MonitorInfo>() == 104;
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
    [StructLayout(LayoutKind.Sequential)] struct Luid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential)] struct Rational { public uint Numerator, Denominator; }
    [StructLayout(LayoutKind.Sequential)] struct SourceInfo { public Luid Adapter; public uint Id, ModeIndex, Status; }
    [StructLayout(LayoutKind.Sequential)] struct TargetInfo { public Luid Adapter; public uint Id, ModeIndex, Technology, Rotation, Scaling; public Rational Refresh; public uint ScanOrder; public int Available; public uint Status; }
    [StructLayout(LayoutKind.Sequential)] struct DisplayPath { public SourceInfo Source; public TargetInfo Target; public uint Flags; }
    // MODE_INFO contains a 48-byte native union, unused here; retain its exact stride.
    [StructLayout(LayoutKind.Explicit, Size = 64)] struct DisplayMode { [FieldOffset(0)] public uint Type; }
    [StructLayout(LayoutKind.Sequential)] struct DeviceHeader { public uint Type, Size; public Luid Adapter; public uint Id; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct SourceName { public DeviceHeader Header; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct TargetName
    {
        public DeviceHeader Header; public uint Flags, Technology; public ushort Manufacturer, Product; public uint Connector;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Path;
    }
    [StructLayout(LayoutKind.Sequential)] struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct MonitorInfo
    {
        public int Size; public NativeRect Monitor, Work; public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref NativeRect rect, IntPtr data);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [DllImport("kernel32.dll")] static extern bool GetPhysicallyInstalledSystemMemory(out ulong memoryKib);
    [DllImport("kernel32.dll")] static extern uint GetActiveProcessorCount(ushort group);
    [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorCallback callback, IntPtr data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string? device, uint index, ref DisplayDevice info, uint flags);
    [DllImport("shcore.dll")] static extern int GetScaleFactorForMonitor(IntPtr monitor, out int scale);
    [DllImport("user32.dll")] static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
    [DllImport("user32.dll")] static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathArray, ref uint modes, [Out] DisplayMode[] modeArray, IntPtr topology);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DisplayConfigGetDeviceInfo(ref SourceName info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int DisplayConfigGetDeviceInfo(ref TargetName info);
}
