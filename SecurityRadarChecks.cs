using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace TokuControl;

internal static class SecurityRadarChecks
{
    internal sealed class Probe : IRadarProbe
    {
        public Func<RadarSource, CancellationToken, Task<RadarRead>> Read = (s, _) => Task.FromResult(Complete(s));
        public int Calls;
        public Task<RadarRead> ReadAsync(RadarSource source, CancellationToken cancellation) { Calls++; cancellation.ThrowIfCancellationRequested(); return Read(source, cancellation); }
    }
    static RadarRead Complete(RadarSource source, params RadarRow[] rows) => new(source, RadarCoverage.Complete, DateTimeOffset.Now, "fixture", rows);
    public static async Task<Dictionary<string, bool>> Run()
    {
        var result = new Dictionary<string, bool>();
        var before = Complete(RadarSource.RunKeys, new("a", "Alpha", "old"), new("b", "Beta", "same"), new("c", "Removed", "old"));
        var after = Complete(RadarSource.RunKeys, new("a", "Alpha", "new"), new("b", "Beta", "same"), new("d", "Added", "new"));
        var changes = RadarAnalysis.Compare(before, after);
        result["radar explicit add change remove"] = changes.Length == 3 && changes.Single(c => c.Kind == "CHANGED").Before == "old" && changes.Any(c => c.Kind == "ADDED") && changes.Any(c => c.Kind == "REMOVED");
        result["radar first inventory is not suspicious"] = RadarAnalysis.Assess(before).Signal == RadarSignal.NotCompared;
        result["radar unchanged is observation not assurance"] = RadarAnalysis.Assess(before, before).Signal == RadarSignal.Observed;
        foreach (var coverage in new[] { RadarCoverage.Partial, RadarCoverage.Unavailable, RadarCoverage.Cancelled })
        {
            var failed = after with { Coverage = coverage, Rows = [] };
            result["radar " + coverage + " never infers deletion"] = RadarAnalysis.Compare(before, failed).Length == 0 && RadarAnalysis.Compare(failed, before).Length == 0 && RadarAnalysis.Assess(failed, before).Signal == RadarSignal.Unknown;
        }
        result["radar compares values exactly but keys case insensitive"] = RadarAnalysis.Compare(Complete(RadarSource.RunKeys, new RadarRow("A", "X", "value")), Complete(RadarSource.RunKeys, new RadarRow("a", "X", "Value"))).Single().Kind == "CHANGED";
        result["radar cross-source comparisons rejected"] = RadarAnalysis.Compare(before, after with { Source = RadarSource.Services }).Length == 0;
        var demo = new DemoRadarProbe();
        var defender = await demo.ReadAsync(RadarSource.Defender, default);
        result["radar enabled defender observation"] = RadarAnalysis.Assess(defender).Signal == RadarSignal.Observed;
        result["radar absent defender field unknown"] = RadarAnalysis.Assess(defender with { Rows = defender.Rows.Where(r => r.Key != "IsTamperProtected").ToArray() }).Signal == RadarSignal.Unknown;
        result["radar disabled defender review"] = RadarAnalysis.Assess(defender with { Rows = defender.Rows.Select(r => r.Key == "AntivirusEnabled" ? r with { Value = "False" } : r).ToArray() }).Signal == RadarSignal.Review;
        result["radar malformed defender not enabled"] = RadarAnalysis.Assess(defender with { Rows = defender.Rows.Select(r => r.Key == "IsTamperProtected" ? r with { Value = "Unknown" } : r).ToArray() }).Signal == RadarSignal.Unknown;
        result["radar absent firewall profile unknown"] = RadarAnalysis.Assess(Complete(RadarSource.Firewall, new RadarRow("Public", "Public", "True"))).Signal == RadarSignal.Unknown;
        result["radar disabled firewall review"] = RadarAnalysis.Assess(Complete(RadarSource.Firewall, new RadarRow("Public", "Public", "False"))).Signal == RadarSignal.Review;
        result["radar zero auth events observational"] = RadarAnalysis.Assess(Complete(RadarSource.Authentication)).Signal == RadarSignal.Observed;
        result["radar logon failure review not compromise"] = RadarAnalysis.Assess(Complete(RadarSource.Authentication, new RadarRow("1", "4625 / logon type 3", "fixture"))).Signal == RadarSignal.Review;
        result["radar cleared log review"] = RadarAnalysis.Assess(Complete(RadarSource.Authentication, new RadarRow("1", "1102 / logon type n/a", "fixture"))).Signal == RadarSignal.Review;
        result["radar remote connection never automatically malicious"] = RadarAnalysis.Assess(await demo.ReadAsync(RadarSource.Tcp, default)).Signal == RadarSignal.Observed;
        var probe = new Probe(); var session = new RadarSession(probe);
        result["radar no baseline before reading"] = !session.SetReference() && probe.Calls == 0;
        await session.RefreshAsync(default);
        result["radar reads all sources without auto baseline"] = probe.Calls == 9 && session.Reference.Count == 0;
        result["radar manual baseline"] = session.SetReference() && session.Reference.Count == 4;
        var reference = session.Reference[RadarSource.RunKeys];
        probe.Read = (s, _) => throw new UnauthorizedAccessException();
        await session.RefreshAsync(default);
        result["radar failed refresh replaces stale current and keeps baseline"] = session.Current.Values.All(r => r.Coverage == RadarCoverage.Unavailable) && ReferenceEquals(reference, session.Reference[RadarSource.RunKeys]) && !session.SetReference();
        var gate = new TaskCompletionSource();
        probe.Read = async (s, token) => { await gate.Task.WaitAsync(token); return Complete(s); };
        var pending = session.RefreshAsync(default);
        var calls = probe.Calls; await session.RefreshAsync(default);
        result["radar concurrent refresh and baseline blocked"] = probe.Calls == calls && !session.SetReference();
        gate.SetResult(); await pending;
        using var cancel = new CancellationTokenSource(); cancel.Cancel(); await session.RefreshAsync(cancel.Token);
        result["radar cancellation finishes all sources honestly"] = !session.Busy && session.Current.Count == 9 && session.Current.Values.All(r => r.Coverage == RadarCoverage.Cancelled);
        var json = "{\"State\":\"Complete\",\"CapturedAt\":\"2026-11-01T00:00:00Z\",\"Note\":\"fixture\",\"Rows\":[{\"Key\":\"a\",\"Label\":\"日本語\",\"Value\":\"$(do not execute)\"}]}";
        result["radar unicode and command-looking strings remain data"] = WindowsRadarProbe.Parse(RadarSource.RunKeys, json).Rows.Single().Value == "$(do not execute)";
        bool Throws(string value) { try { WindowsRadarProbe.Parse(RadarSource.RunKeys, value); return false; } catch { return true; } }
        result["radar malformed coverage rejected"] = Throws(json.Replace("Complete", "Safe")) && Throws(json.Replace("Complete", "999"));
        result["radar duplicate keys rejected"] = Throws(json.Replace("}]", "},{\"Key\":\"A\",\"Label\":\"duplicate\",\"Value\":\"x\"}]"));
        result["radar invalid JSON rejected"] = Throws("{broken");
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("12345")); using var reader = new StreamReader(stream);
        try { await WindowsRadarProbe.BoundedReadAsync(reader, 4, default); result["radar output memory bound"] = false; } catch (InvalidDataException) { result["radar output memory bound"] = true; }
        var info = WindowsRadarProbe.StartInfo(RadarSource.Tcp);
        result["radar trusted fixed probe without elevation or policy bypass"] = Path.IsPathFullyQualified(info.FileName) && !info.UseShellExecute && info.CreateNoWindow && info.Verb != "runas" && info.ArgumentList.Contains("-NoProfile") && !info.ArgumentList.Contains("-ExecutionPolicy") && !info.Environment["PSModulePath"]!.Contains("Users", StringComparison.OrdinalIgnoreCase);
        return result;
    }
    public static int RunCli(string[] args, bool live)
    {
        var index = Array.IndexOf(args, "--result");
        if (index < 0 || index + 1 >= args.Length) return 2;
        var checks = Run().GetAwaiter().GetResult();
        foreach (var test in RoutingChecks.Run().GetAwaiter().GetResult()) checks[test.Key] = test.Value;
        foreach (var test in LauncherChecks.Run()) checks[test.Key] = test.Value;
        foreach (var test in UpdaterChecks.Run().GetAwaiter().GetResult()) checks[test.Key] = test.Value;
        var report = new Dictionary<string, object>(checks.ToDictionary(p => p.Key, p => (object)p.Value));
        if (live)
        {
            var session = new RadarSession(new WindowsRadarProbe()); session.RefreshAsync(default).GetAwaiter().GetResult();
            // Test evidence contains only status/count metadata, never accounts, IPs or commands.
            report["sources"] = session.Current.Values.Select(r => new { source = r.Source.ToString(), coverage = r.Coverage.ToString(), count = r.Rows.Length, r.CapturedAt, r.Note }).ToArray();
            report["live collection completed"] = session.Current.Count == 9;
        }
        report["passed"] = checks.Values.All(v => v);
        var path = Path.GetFullPath(args[index + 1]); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return checks.Values.All(v => v) ? 0 : 1;
    }
}

public sealed partial class ConsoleWindow
{
    public async Task CaptureRadarChecks(string directory)
    {
        Directory.CreateDirectory(directory); var checks = new Dictionary<string, object>();
        try
        {
            SelectSection("security"); var first = radarWindow!; SelectSection("security");
            checks["singleton"] = ReferenceEquals(first, radarWindow);
            checks["no scan on opening"] = radarSession!.Current.Count == 0;
            checks["radar visible and contained"] = first.IsVisible && first.ShowInTaskbar && WindowPlacement.WorkArea(first).Contains(WindowPlacement.NativeBounds(first));
            await first.RefreshAsync(); first.UpdateLayout();
            checks["nine demo sources"] = first.Session.Current.Count == 9 && first.CardCount == 7;
            checks["baseline button works"] = first.ReferenceButton.IsEnabled;
            first.ReferenceButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            checks["explicit reference set"] = first.Session.Reference.Count == 4;
            first.SelectCard(2);
            checks["map opens evidence"] = first.DetailText.Contains("T1547.001") && first.DetailText.Contains("DEMO");
            Capture(first, Path.Combine(directory, "radar-map.png"));
            first.SetView(true); first.SelectCard(7);
            checks["source details"] = first.CardCount == 9 && first.DetailText.Contains("203.0.113.20") && first.DetailText.Contains("OBSERVED");
            first.Width = 760; first.Height = 540; first.UpdateLayout();
            checks["toolbar fits small window"] = first.ReferenceButton.TranslatePoint(new Point(first.ReferenceButton.ActualWidth, 0), first).X < first.ActualWidth;
            Capture(first, Path.Combine(directory, "radar-small.png"));
            first.Close(); SelectSection("security");
            checks["reopen preserves session reference"] = radarWindow != null && !ReferenceEquals(first, radarWindow) && radarWindow.Session.Reference.Count == 4;
            radarWindow!.Close();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var delayed = new SecurityRadarChecks.Probe { Read = async (s, _) => { await gate.Task; return new RadarRead(s, RadarCoverage.Complete, DateTimeOffset.Now, "delayed fixture", []); } };
            radarSession = new(delayed); ShowSecurityRadar();
            var closing = radarWindow!; var pending = closing.RefreshAsync();
            closing.Close(); ShowSecurityRadar(); var reopened = radarWindow!;
            checks["reopened window waits for active read"] = !reopened.RefreshButton.IsEnabled;
            gate.SetResult(); await pending;
            checks["reopened window observes completion"] = reopened.RefreshButton.IsEnabled && !reopened.CancelButton.IsEnabled && reopened.Session.Current.Count == 9;
            reopened.Close();
        }
        catch (Exception ex) { checks["error"] = ex.ToString(); }
        checks["passed"] = checks.Count >= 9 && checks.Values.All(v => v is true);
        File.WriteAllText(Path.Combine(directory, "radar-ui-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
        Application.Current.Shutdown(checks["passed"] is true ? 0 : 1);
    }
}
