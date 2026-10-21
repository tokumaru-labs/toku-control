using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace TokuControl;

public record AudioState(bool Available, float Volume, bool Muted, string Device, string Error = "");
public interface IAudioBackend : IDisposable
{
    AudioState Read();
    AudioState SetVolume(float volume);
    AudioState ToggleMute();
    float[] Peaks();
}

// All COM access stays on the WPF dispatcher. Each write checks the current
// eRender / eMultimedia endpoint, so a stale slider never writes to an old device.
public sealed class CoreAudio : IAudioBackend
{
    IMMDeviceEnumerator? enumerator;
    IMMDevice? device;
    IAudioEndpointVolume? volume;
    IAudioMeterInformation? meter;
    string id = "";
    static void Check(int result) { Marshal.ThrowExceptionForHR(result); }
    static void Release(object? obj) { if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
    bool Connect()
    {
        enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
        Check(enumerator.GetDefaultAudioEndpoint(0, 1, out var current));
        try
        {
            Check(current.GetId(out var currentId));
            if (currentId == id && volume != null) return false;
            Release(meter); meter = null; Release(volume); volume = null; Release(device); device = null;
            id = "";
            var volumeGuid = typeof(IAudioEndpointVolume).GUID;
            Check(current.Activate(ref volumeGuid, 23, IntPtr.Zero, out var v));
            volume = (IAudioEndpointVolume)v;
            var meterGuid = typeof(IAudioMeterInformation).GUID;
            if (current.Activate(ref meterGuid, 23, IntPtr.Zero, out var m) >= 0) meter = (IAudioMeterInformation)m;
            device = current; current = null!; id = currentId;
            return true;
        }
        finally { Release(current); }
    }
    AudioState State()
    {
        Check(volume!.GetMasterVolumeLevelScalar(out var value));
        Check(volume.GetMute(out var muted));
        return new(true, value, muted, id);
    }
    AudioState Failed(Exception ex)
    {
        Release(meter); meter = null; Release(volume); volume = null; Release(device); device = null; id = "";
        return new(false, 0, false, "", $"UNAVAILABLE / 0x{ex.HResult:X8}");
    }
    public AudioState Read() { try { Connect(); return State(); } catch (Exception ex) { return Failed(ex); } }
    public AudioState SetVolume(float value)
    {
        try
        {
            if (Connect()) return State() with { Error = "DEVICE CHANGED / retry on current output" };
            var context = Guid.Empty;
            Check(volume!.SetMasterVolumeLevelScalar(Math.Clamp(value, 0, 1), ref context));
            return State();
        }
        catch (Exception ex) { var actual = Read(); return actual with { Error = $"VOLUME FAILED / 0x{ex.HResult:X8}" }; }
    }
    public AudioState ToggleMute()
    {
        try
        {
            if (Connect()) return State() with { Error = "DEVICE CHANGED / retry on current output" };
            var before = State(); var context = Guid.Empty;
            Check(volume!.SetMute(!before.Muted, ref context)); return State();
        }
        catch (Exception ex) { var actual = Read(); return actual with { Error = $"MUTE FAILED / 0x{ex.HResult:X8}" }; }
    }
    public float[] Peaks()
    {
        try
        {
            if (meter == null) return [];
            Check(meter.GetMeteringChannelCount(out var count));
            if (count == 2) { var values = new float[2]; Check(meter.GetChannelsPeakValues(2, values)); return values; }
            Check(meter.GetPeakValue(out var peak)); return [peak];
        }
        catch { return []; }
    }
    public void Dispose() { Release(meter); Release(volume); Release(device); Release(enumerator); meter = null; volume = null; device = null; enumerator = null; }
}

public sealed class PreviewAudio : IAudioBackend
{
    readonly PreviewOutputRoutes routes;
    readonly Dictionary<string, AudioState> states;
    public PreviewAudio(PreviewOutputRoutes? routes = null)
    {
        this.routes = routes ?? new PreviewOutputRoutes();
        states = this.routes.List().ToDictionary(x => x.Id, x => new AudioState(true, x.Volume ?? 0, x.Muted ?? false, x.Id));
    }
    public AudioState Read() => states[routes.Default(1)];
    public AudioState SetVolume(float value) { var state = Read(); return states[state.Device] = state with { Volume = Math.Clamp(value, 0, 1) }; }
    public AudioState ToggleMute() { var state = Read(); return states[state.Device] = state with { Muted = !state.Muted }; }
    public float[] Peaks() => [.18f, .26f];
    public void Dispose() { }
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] class MMDeviceEnumerator { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, int mask, out IMMDeviceCollection collection);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, int context, IntPtr activation, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
    [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out int state);
}
[ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioEndpointVolume
{
    [PreserveSig] int RegisterControlChangeNotify(IntPtr callback);
    [PreserveSig] int UnregisterControlChangeNotify(IntPtr callback);
    [PreserveSig] int GetChannelCount(out uint count);
    [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
    [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
    [PreserveSig] int GetMasterVolumeLevel(out float level);
    [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    [PreserveSig] int SetChannelVolumeLevel(uint channel, float level, ref Guid context);
    [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
    [PreserveSig] int GetChannelVolumeLevel(uint channel, out float level);
    [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
    [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
    [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    [PreserveSig] int GetVolumeStepInfo(out uint step, out uint count);
    [PreserveSig] int VolumeStepUp(ref Guid context);
    [PreserveSig] int VolumeStepDown(ref Guid context);
    [PreserveSig] int QueryHardwareSupport(out uint mask);
    [PreserveSig] int GetVolumeRange(out float min, out float max, out float increment);
}
[ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IAudioMeterInformation
{
    [PreserveSig] int GetPeakValue(out float peak);
    [PreserveSig] int GetMeteringChannelCount(out int count);
    [PreserveSig] int GetChannelsPeakValues(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peaks);
    [PreserveSig] int QueryHardwareSupport(out int mask);
}

public record Pulse(double? Cpu, double? Ram, double? Disk);
public sealed class SystemProbe
{
    ulong lastIdle, lastTotal;
    public Pulse Read()
    {
        double? cpu = null, ram = null, disk = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var total = kernel + user;
            if (lastTotal != 0 && total > lastTotal) cpu = Math.Clamp(100.0 * (1 - (double)(idle - lastIdle) / (total - lastTotal)), 0, 100);
            lastIdle = idle; lastTotal = total;
        }
        var memory = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
        if (GlobalMemoryStatusEx(ref memory)) ram = memory.Load;
        try { var d = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory)!); disk = 100.0 * (d.TotalSize - d.TotalFreeSpace) / d.TotalSize; } catch { }
        return new(cpu, ram, disk);
    }
    [DllImport("kernel32.dll")] static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
    [StructLayout(LayoutKind.Sequential)] struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPage, AvailablePage, TotalVirtual, AvailableVirtual, Extended; }
}

public static class OpticalDrives
{
    static readonly SemaphoreSlim gate = new(1, 1);
    public static string[] Scan() => DriveInfo.GetDrives().Where(d => GetDriveType(d.Name) == 5).Select(d => d.Name).ToArray();
    public static async Task<string> Command(string root, bool eject)
    {
        if (!await gate.WaitAsync(0)) return "BUSY / previous command still active";
        var worker = Task.Run(() =>
        {
            try
            {
                if (!Scan().Contains(root, StringComparer.OrdinalIgnoreCase) || GetDriveType(root) != 5) return "UNAVAILABLE / not an optical drive";
                using var h = CreateFile(@"\\.\" + root.TrimEnd('\\'), 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (h.IsInvalid) return Error(Marshal.GetLastWin32Error());
                if (!DeviceIoControl(h, eject ? 0x2D4808u : 0x2D480Cu, IntPtr.Zero, 0, IntPtr.Zero, 0, out _, IntPtr.Zero)) return Error(Marshal.GetLastWin32Error());
                return "COMMAND ACCEPTED / STATE UNKNOWN";
            }
            catch (Exception ex) { return "UNAVAILABLE / " + ex.GetType().Name; }
            finally { gate.Release(); }
        });
        return await Task.WhenAny(worker, Task.Delay(8000)) == worker ? await worker : "TIMEOUT / command may still be pending";
    }
    static string Error(int code) => code switch { 5 => "ACCESS DENIED", 1 or 50 => "UNSUPPORTED", 21 or 1112 => "NOT READY / NO MEDIA", 170 => "BUSY", _ => "FAILED / WIN32 " + code };
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern uint GetDriveType(string root);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern SafeFileHandle CreateFile(string file, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, uint inputLength, IntPtr output, uint outputLength, out uint returned, IntPtr overlapped);
}

// Size is in WPF device-independent pixels; positions remain native pixels.
public record WindowPosition(double X, double Y, bool Pin, double? Width = null, double? Height = null);
public sealed class Settings
{
    public bool Crt { get; set; } = true;
    public double Scanline { get; set; } = .08;
    public double Bloom { get; set; } = .22;
    public double Noise { get; set; } = .025;
    public double Rgb { get; set; } = .25;
    public double Vignette { get; set; } = .15;
    public double TextSize { get; set; } = 12;
    public Dictionary<string, WindowPosition> Windows { get; set; } = new();
    public void Normalize()
    {
        double Clean(double value, double min, double max, double fallback) => double.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;
        Scanline = Clean(Scanline, 0, .18, .08); Bloom = Clean(Bloom, 0, .5, .22);
        Noise = Clean(Noise, 0, .08, .025); Rgb = Clean(Rgb, 0, .5, .25);
        Vignette = Clean(Vignette, 0, .35, .15); TextSize = Clean(TextSize, 11, 14, 12);
        Windows ??= new();
        double? CleanSize(double? value) => value is > 0 && double.IsFinite(value.Value) ? Math.Min(value.Value, 4096) : null;
        Windows = Windows.Where(p => p.Value != null && double.IsFinite(p.Value.X) && double.IsFinite(p.Value.Y))
            .ToDictionary(p => p.Key, p => p.Value with { Width = CleanSize(p.Value.Width), Height = CleanSize(p.Value.Height) });
    }
}
public sealed class SettingsStore(string directory)
{
    public string DirectoryPath => directory;
    public string FilePath => Path.Combine(directory, "settings.json");
    public string? Warning { get; private set; }
    public Settings Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var result = JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? throw new JsonException("Empty settings");
            result.Normalize(); return result;
        }
        catch (Exception ex)
        {
            Warning = "SETTINGS RESET / " + ex.GetType().Name;
            try { if (File.Exists(FilePath)) File.Copy(FilePath, FilePath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false); } catch { }
            return new();
        }
    }
    public string? Save(Settings value)
    {
        try
        {
            value.Normalize(); Directory.CreateDirectory(directory);
            var temp = FilePath + ".tmp-" + Guid.NewGuid().ToString("N");
            File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, FilePath, true); return null;
        }
        catch (Exception ex) { return "SETTINGS SAVE FAILED / " + ex.GetType().Name; }
    }
}

public sealed class RingLog(int capacity = 1000)
{
    readonly Queue<string> lines = new();
    public IReadOnlyCollection<string> Lines => lines;
    public void Add(string level, string message)
    {
        lines.Enqueue($"[{DateTime.Now:HH:mm:ss}] [{level,-5}] {message}");
        while (lines.Count > capacity) lines.Dequeue();
    }
    public void Clear() => lines.Clear();
}
