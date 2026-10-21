using System.Runtime.InteropServices;

namespace TokuControl;

public record PlaybackOutput(string Id, string Name, float? Volume, bool? Muted)
{
    public override string ToString() => Name;
}
public record OutputSwitchResult(bool Success, string Message);
public interface IOutputRoutes : IDisposable
{
    PlaybackOutput[] List();
    string Default(int role);
    void SetDefault(string id, int role);
}

// Only Console and Multimedia are changed. Capture and Communications are never set.
// This is deliberately separate from the volume backend: a routing operation must
// not change either endpoint's saved volume or mute setting.
public sealed class OutputSwitcher(IOutputRoutes routes)
{
    bool busy;
    public async Task<OutputSwitchResult> Switch(string id)
    {
        if (busy) return new(false, "BUSY / output switch in progress");
        busy = true;
        string[]? before = null;
        var attempted = new List<int>();
        try
        {
            var target = routes.List().SingleOrDefault(x => x.Id == id);
            if (target == null) return new(false, "UNAVAILABLE / output disconnected; rescan");
            before = [routes.Default(0), routes.Default(1)];
            if (before.Any(string.IsNullOrEmpty)) return new(false, "UNAVAILABLE / cannot capture previous output");
            if (before.All(x => x == id)) return new(true, "ALREADY DEFAULT / " + target.Name);
            foreach (int role in new[] { 0, 1 })
            {
                if (before[role] == id) continue;
                attempted.Add(role); // include a call that changes state and then fails
                routes.SetDefault(id, role);
            }
            if (!await WaitFor(() => routes.Default(0) == id && routes.Default(1) == id)) throw new InvalidOperationException("default readback mismatch");
            return new(true, "OUTPUT VERIFIED / " + target.Name);
        }
        catch (Exception ex)
        {
            bool restoreFailed = false;
            if (before != null)
            {
                foreach (int role in attempted.AsEnumerable().Reverse())
                {
                    try
                    {
                        var current = routes.Default(role);
                        // Don't overwrite an unrelated concurrent change made in Windows.
                        if (current == id) routes.SetDefault(before[role], role);
                        else if (current != before[role]) restoreFailed = true;
                    }
                    catch { restoreFailed = true; }
                }
                try { restoreFailed |= !await WaitFor(() => routes.Default(0) == before[0] && routes.Default(1) == before[1]); }
                catch { restoreFailed = true; }
            }
            string status = before == null ? "SWITCH UNAVAILABLE" : restoreFailed ? "SWITCH FAILED / CHECK WINDOWS OUTPUT" : "SWITCH FAILED / PREVIOUS OUTPUT RESTORED";
            return new(false, status + $" / 0x{ex.HResult:X8}");
        }
        finally { busy = false; }
    }
    static async Task<bool> WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 12; i++) { if (condition()) return true; await Task.Delay(50); }
        return false;
    }
}

public sealed class WindowsOutputRoutes : IOutputRoutes
{
    IMMDeviceEnumerator? enumerator;
    IMMDeviceEnumerator Enumerator => enumerator ??= (IMMDeviceEnumerator)new MMDeviceEnumerator();
    IPolicyConfig? policy;
    static void Check(int result) => Marshal.ThrowExceptionForHR(result);
    static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public PlaybackOutput[] List()
    {
        Check(Enumerator.EnumAudioEndpoints(0, 1, out var collection)); // render, active only
        var result = new List<PlaybackOutput>();
        try
        {
            Check(collection.GetCount(out var count));
            for (uint i = 0; i < count; i++)
            {
                Check(collection.Item(i, out var endpoint));
                try
                {
                    Check(endpoint.GetId(out var id));
                    var name = FriendlyName(endpoint, id);
                    float? level = null; bool? muted = null;
                    var iid = typeof(IAudioEndpointVolume).GUID;
                    if (endpoint.Activate(ref iid, 23, IntPtr.Zero, out var value) >= 0)
                    {
                        try
                        {
                            var v = (IAudioEndpointVolume)value;
                            if (v.GetMasterVolumeLevelScalar(out var f) >= 0) level = f;
                            if (v.GetMute(out var m) >= 0) muted = m;
                        }
                        finally { Release(value); }
                    }
                    result.Add(new(id, name, level, muted));
                }
                finally { Release(endpoint); }
            }
        }
        finally { Release(collection); }
        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
    static string FriendlyName(IMMDevice endpoint, string fallback)
    {
        if (endpoint.OpenPropertyStore(0, out var store) < 0) return fallback;
        try
        {
            var key = new PropertyKey { Format = new("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
            if (store.GetValue(ref key, out var value) < 0) return fallback;
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? fallback : fallback; }
            finally { PropVariantClear(ref value); }
        }
        finally { Release(store); }
    }
    public string Default(int role)
    {
        Check(Enumerator.GetDefaultAudioEndpoint(0, role, out var endpoint));
        try { Check(endpoint.GetId(out var id)); return id; }
        finally { Release(endpoint); }
    }
    public void SetDefault(string id, int role)
    {
        if (role is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(role));
        if (!List().Any(x => x.Id == id)) throw new InvalidOperationException("Output no longer active");
        // IPolicyConfig is an undocumented Windows compatibility interface.
        // Never create it or call it during startup, enumeration, or automated native QA.
        policy ??= (IPolicyConfig)new PolicyConfigClient();
        Check(policy.SetDefaultEndpoint(id, role));
    }
    public void Dispose() { Release(policy); policy = null; Release(enumerator); enumerator = null; }
    [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropertyValue value);
}

public sealed class PreviewOutputRoutes : IOutputRoutes
{
    readonly string[] defaults = ["preview-a", "preview-a", "preview-communications"];
    public readonly List<(string Id, int Role)> Writes = new();
    public PlaybackOutput[] Outputs { get; set; } = [new("preview-a", "PREVIEW / SPEAKERS A", .34f, false), new("preview-b", "PREVIEW / HEADPHONES B", .22f, true)];
    public Action<string, int>? BeforeWrite { get; set; }
    public bool IgnoreWrites { get; set; }
    public PlaybackOutput[] List() => Outputs;
    public string Default(int role) => defaults[role];
    public void SetDefault(string id, int role)
    {
        if (role is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(role));
        Writes.Add((id, role)); BeforeWrite?.Invoke(id, role);
        if (!IgnoreWrites) defaults[role] = id;
    }
    public void Dispose() { }
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}
[StructLayout(LayoutKind.Sequential)] struct PropertyKey { public Guid Format; public uint Id; }
[StructLayout(LayoutKind.Explicit, Size = 24)] struct PropertyValue { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public IntPtr Pointer; }
[ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropertyValue value);
}
[ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }
// ABI slot order independently declared for the one operation used here. The
// unused slots cannot be called through this interface. ABI cross-check source:
// https://github.com/Belphemur/SoundSwitch/blob/dev/SoundSwitch.Audio.Manager/Interop/Interface/Policy/IPolicyConfig.cs
[ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    void UnusedGetMixFormat(); void UnusedGetDeviceFormat(); void UnusedResetDeviceFormat();
    void UnusedSetDeviceFormat(); void UnusedGetProcessingPeriod(); void UnusedSetProcessingPeriod();
    void UnusedGetShareMode(); void UnusedSetShareMode(); void UnusedGetPropertyValue(); void UnusedSetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string device, int role);
}

public static class RoutingChecks
{
    public static async Task<Dictionary<string, bool>> Run()
    {
        var result = new Dictionary<string, bool>();
        using var happy = new PreviewOutputRoutes(); var s = new OutputSwitcher(happy);
        var originalLevels = happy.List().Select(x => (x.Volume, x.Muted)).ToArray();
        var changed = await s.Switch("preview-b");
        result["switchReadback"] = changed.Success && happy.Default(0) == "preview-b" && happy.Default(1) == "preview-b";
        result["communicationsUnchanged"] = happy.Default(2) == "preview-communications" && happy.Writes.All(x => x.Role is 0 or 1);
        result["switchPreservesLevels"] = happy.List().Select(x => (x.Volume, x.Muted)).SequenceEqual(originalLevels);
        int count = happy.Writes.Count; await s.Switch("preview-b"); result["sameOutputNoWrite"] = happy.Writes.Count == count;
        var missing = await s.Switch("disconnected"); result["disconnectedOutputNoWrite"] = !missing.Success && happy.Writes.Count == count;
        using var broken = new PreviewOutputRoutes { BeforeWrite = (id, role) => { if (id == "preview-b" && role == 1) throw new COMException("test denial", unchecked((int)0x80070005)); } };
        var rolled = await new OutputSwitcher(broken).Switch("preview-b");
        result["partialFailureRollback"] = !rolled.Success && rolled.Message.Contains("RESTORED") && broken.Default(0) == "preview-a" && broken.Default(1) == "preview-a";
        using var ignored = new PreviewOutputRoutes { IgnoreWrites = true };
        var ignoredResult = await new OutputSwitcher(ignored).Switch("preview-b"); result["noFalseSuccessOnIgnoredWrite"] = !ignoredResult.Success;
        using var blocked = new PreviewOutputRoutes { BeforeWrite = (id, role) => { if (id == "preview-a" || role == 1) throw new InvalidOperationException("test rollback denial"); } };
        var blockedResult = await new OutputSwitcher(blocked).Switch("preview-b");
        result["rollbackFailureExplicit"] = !blockedResult.Success && blockedResult.Message.Contains("CHECK WINDOWS OUTPUT");
        return result;
    }
}
