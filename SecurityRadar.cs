using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;

namespace TokuControl;

internal enum RadarSource { Defender, Firewall, RunKeys, StartupFolders, Services, ScheduledTasks, Authentication, Tcp, Udp }
internal enum RadarCoverage { Complete, Partial, Unavailable, Cancelled }
internal enum RadarSignal { Observed, Review, Unknown, NotCompared }
internal sealed record RadarRow(string Key, string Label, string Value);
internal sealed record RadarRead(RadarSource Source, RadarCoverage Coverage, DateTimeOffset CapturedAt, string Note, RadarRow[] Rows);
internal sealed record RadarAssessment(RadarSignal Signal, string Summary, string Explanation);
internal sealed record RadarChange(string Kind, string Name, string Before, string After);
internal sealed record RadarTechnique(string Id, string Title, RadarSource[] Sources, string Explanation, string Url);

internal static class RadarKnowledge
{
    public static readonly RadarSource[] Persistence = [RadarSource.RunKeys, RadarSource.StartupFolders, RadarSource.Services, RadarSource.ScheduledTasks];
    public static readonly RadarTechnique[] Techniques =
    [
        new("T1685", "保護機能の妨害", [RadarSource.Defender], "Defender の設定状態を確認。回避や改ざんの実行を検出するものではありません。", "https://attack.mitre.org/techniques/T1685/"),
        new("T1686", "ファイアウォールの変更", [RadarSource.Firewall], "各プロファイルの有効状態。許可ルールや実際の到達可能性は未検証です。", "https://attack.mitre.org/techniques/T1686/"),
        new("T1547.001", "ログオン時の自動起動", [RadarSource.RunKeys, RadarSource.StartupFolders], "Run / RunOnce と Startup の変化を比較。正規インストール・更新でも変わります。", "https://attack.mitre.org/techniques/T1547/001/"),
        new("T1543.003", "Windows サービス", [RadarSource.Services], "実行パス・起動方式・実行アカウントの差分。サービスの存在だけでは悪性と判断しません。", "https://attack.mitre.org/techniques/T1543/003/"),
        new("T1053.005", "スケジュールされたタスク", [RadarSource.ScheduledTasks], "取得できたタスク定義を比較。権限で見えないタスクが残る可能性があります。", "https://attack.mitre.org/techniques/T1053/005/"),
        new("T1110", "認証試行の確認", [RadarSource.Authentication], "直近24時間の4624・4625・1102。失敗は入力ミスや古い保存資格情報でも発生します。", "https://attack.mitre.org/techniques/T1110/"),
        new("T1071", "通信の手がかり", [RadarSource.Tcp, RadarSource.Udp], "現在のTCP接続とUDP待受。通信内容・C2・情報流出・アプリ層プロトコルは判定しません。", "https://attack.mitre.org/techniques/T1071/")
    ];
    public static string Title(RadarSource source) => source switch
    {
        RadarSource.Defender => "Windows Defender", RadarSource.Firewall => "Windows Firewall",
        RadarSource.RunKeys => "Run / RunOnce", RadarSource.StartupFolders => "Startup folders",
        RadarSource.Services => "Services", RadarSource.ScheduledTasks => "Scheduled tasks",
        RadarSource.Authentication => "Authentication / 24h", RadarSource.Tcp => "TCP connections", _ => "UDP endpoints"
    };
    public static string NextCheck(RadarSource source, RadarSignal signal)
    {
        string check = source switch
        {
            RadarSource.Defender => "Windows セキュリティで使用中の保護プロバイダー、リアルタイム保護、定義ファイルの更新日時を確認する。他社製AVを使用中なら、その製品の保護状態も確かめる。",
            RadarSource.Firewall => "Windows セキュリティの「ファイアウォールとネットワーク保護」で使用中のプロファイルを確認し、対応する Domain・Private・Public の有効状態を照合する。組織管理の端末では管理方針も確かめる。",
            RadarSource.RunKeys => "Run / RunOnce の値名と登録コマンドのパス・引数を、最近インストールまたは更新したアプリと照合する。見覚えのないコマンドは実行せず、保護の履歴を確認する。",
            RadarSource.StartupFolders => "自分用・全ユーザー用のどちらの Startup フォルダーかを確かめ、ファイル名を最近追加したアプリと照合する。ショートカットのハッシュが示すのはショートカット自体の内容。",
            RadarSource.Services => "「サービス」のプロパティで実行パス・起動方式・実行アカウントを確認し、そのサービスを登録したソフトの設定と照合する。",
            RadarSource.ScheduledTasks => "タスク スケジューラで同じフォルダーのタスクを探し、「操作」の内容を作成元のソフトと照合する。「プログラムの開始」ならパスと引数を確認する。",
            RadarSource.Authentication => "4625 はログオン失敗。時刻・アカウント・ログオン種別を自分の操作と照合する。4624 の種別10はリモート対話ログオン。心当たりがなければ、その時間帯のリモート利用を確認する。1102 はセキュリティ監査ログの消去。自分や管理者がその時刻に消去したか確認する。",
            RadarSource.Tcp => "接続先と状態を読み、PID とプロセス名をタスク マネージャーの「詳細」で照合する。自分で開いたアプリを通常終了して READ / REFRESH で変化を確認する。海外IPへの接続だけで攻撃と判断しない。",
            RadarSource.Udp => "UDP の待受ポートと PID を確認し、所有プロセスをタスク マネージャーで照合する。通信相手や転送内容はこの一覧では分からない。",
            _ => "取得元と確認できた範囲を確認する。"
        };
        string context = signal switch
        {
            RadarSignal.Unknown => "取得できなかった範囲は未確認。取得済みの行があれば、取得時刻・理由と合わせて読む。",
            RadarSignal.NotCompared => "初回一覧は未比較。永続化の4情報源をすべて取得して内容を確認したら、SET REFERENCE で基準に固定し、同じ起動中に再取得して比べる。",
            RadarSignal.Review when Persistence.Contains(source) => "BEFORE / AFTER を直前のインストールや更新と照合する。構成の変更だけでは侵入は確定しない。",
            RadarSignal.Review => "要確認となった項目を、自分の操作や端末の管理方針と照合する。",
            _ => ""
        };
        return context.Length == 0 ? check : context + Environment.NewLine + check;
    }
    public static string Coverage(RadarCoverage value) => value switch
    { RadarCoverage.Complete => "取得済み / 対象範囲内", RadarCoverage.Partial => "一部のみ取得", RadarCoverage.Cancelled => "中止", _ => "取得できず" };
    public static string Signal(RadarSignal value) => value switch
    { RadarSignal.Observed => "OBSERVED / 観測", RadarSignal.Review => "REVIEW / 要確認", RadarSignal.NotCompared => "NO REFERENCE / 未比較", _ => "UNKNOWN / 未確認" };
}

internal static class RadarAnalysis
{
    public static RadarChange[] Compare(RadarRead reference, RadarRead current)
    {
        // Never turn missing or truncated observations into removals.
        if (reference.Source != current.Source || reference.Coverage != RadarCoverage.Complete || current.Coverage != RadarCoverage.Complete) return [];
        var before = reference.Rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        var after = current.Rows.ToDictionary(r => r.Key, StringComparer.OrdinalIgnoreCase);
        return before.Keys.Union(after.Keys, StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Select(key => !before.TryGetValue(key, out var old) ? new RadarChange("ADDED", after[key].Label, "", after[key].Value)
                : !after.TryGetValue(key, out var next) ? new RadarChange("REMOVED", old.Label, old.Value, "")
                : old.Value != next.Value ? new RadarChange("CHANGED", next.Label, old.Value, next.Value) : null)
            .OfType<RadarChange>().ToArray();
    }
    public static RadarAssessment Assess(RadarRead read, RadarRead? reference = null)
    {
        if (read.Coverage != RadarCoverage.Complete) return new(RadarSignal.Unknown, RadarKnowledge.Coverage(read.Coverage), read.Note + " 取得できた行があっても全体の安全性や削除は判断できません。");
        if (RadarKnowledge.Persistence.Contains(read.Source))
        {
            if (reference == null) return new(RadarSignal.NotCompared, $"{read.Rows.Length} 件 / 比較基準なし", "初回一覧は侵入の証拠ではありません。内容を確認して、このセッションの比較基準に固定できます。");
            var changes = Compare(reference, read);
            return new(changes.Length > 0 ? RadarSignal.Review : RadarSignal.Observed, $"{changes.Length} 件の変化 / {read.Rows.Length} 件の現状", "比較基準からの構成変化です。正規の更新でも発生します。変化なしでも安全の証明にはなりません。");
        }
        var values = read.Rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
        if (read.Source == RadarSource.Defender)
        {
            string[] required = ["AMServiceEnabled", "AntivirusEnabled", "RealTimeProtectionEnabled", "BehaviorMonitorEnabled", "IsTamperProtected"];
            bool missing = required.Any(k => !values.TryGetValue(k, out var v) || !bool.TryParse(v, out _));
            bool disabled = required.Any(k => values.TryGetValue(k, out var v) && bool.TryParse(v, out var b) && !b);
            return new(disabled ? RadarSignal.Review : missing ? RadarSignal.Unknown : RadarSignal.Observed,
                disabled ? "無効の項目あり / 設定を確認" : missing ? "一部の保護状態が不明" : "照会した保護項目は有効",
                "Defender の報告値です。他社AV・パッシブモード・組織ポリシーも確認してください。回避耐性や未感染を保証しません。");
        }
        if (read.Source == RadarSource.Firewall)
        {
            bool missing = new[] { "Domain", "Private", "Public" }.Any(k => !values.TryGetValue(k, out var v) || v is not ("True" or "False"));
            bool disabled = values.Values.Contains("False");
            return new(disabled ? RadarSignal.Review : missing ? RadarSignal.Unknown : RadarSignal.Observed,
                disabled ? "無効のプロファイルあり" : missing ? "プロファイル状態が不明" : "3プロファイル有効",
                "ActiveStore のプロファイル設定です。現在使われていないプロファイルも含みます。許可ルール・外部からの到達性は未検証です。");
        }
        if (read.Source == RadarSource.Authentication)
        {
            int failures = read.Rows.Count(r => r.Label.StartsWith("4625 /", StringComparison.Ordinal));
            int cleared = read.Rows.Count(r => r.Label.StartsWith("1102 /", StringComparison.Ordinal));
            return new(failures > 0 || cleared > 0 ? RadarSignal.Review : RadarSignal.Observed, $"ログオン失敗 {failures} 件 / 監査ログ消去 {cleared} 件 / 対象イベント {read.Rows.Length} 件",
                "24時間の取得範囲のみ。ログオン成功は侵入を意味せず、失敗は入力ミス等でも発生します。監査設定・ログ保持の完全性は未確認。0件は攻撃なしを意味しません。");
        }
        return new(RadarSignal.Observed, $"{read.Rows.Length} 件 / 更新時点の接続・待受", "接続先やポートだけでは悪性判定できません。待受は外部から接続可能という意味ではありません。PIDは再利用され、プロセス情報が取得できない場合もあります。");
    }
}

internal interface IRadarProbe { Task<RadarRead> ReadAsync(RadarSource source, CancellationToken cancellation); }

internal sealed class RadarSession(IRadarProbe probe)
{
    public Dictionary<RadarSource, RadarRead> Current { get; } = new();
    public Dictionary<RadarSource, RadarRead> Reference { get; } = new();
    public bool Busy { get; private set; }
    public event Action? Changed;
    public bool SetReference()
    {
        if (Busy || !RadarKnowledge.Persistence.All(s => Current.TryGetValue(s, out var r) && r.Coverage == RadarCoverage.Complete)) return false;
        Reference.Clear();
        foreach (var source in RadarKnowledge.Persistence) Reference[source] = Current[source];
        Changed?.Invoke();
        return true;
    }
    public async Task RefreshAsync(CancellationToken cancellation)
    {
        if (Busy) return;
        Busy = true;
        // Discard prior current observations on refresh, retaining the explicit reference only.
        Current.Clear();
        Changed?.Invoke();
        try
        {
            foreach (var source in Enum.GetValues<RadarSource>())
            {
                RadarRead read;
                try { read = await probe.ReadAsync(source, cancellation); }
                catch (OperationCanceledException) { read = new(source, RadarCoverage.Cancelled, DateTimeOffset.Now, "ユーザーが取得を中止しました。", []); }
                catch (Exception ex) { read = new(source, RadarCoverage.Unavailable, DateTimeOffset.Now, "取得失敗: " + ex.GetType().Name, []); }
                Current[source] = read; Changed?.Invoke();
            }
        }
        finally { Busy = false; Changed?.Invoke(); }
    }
}

internal sealed class WindowsRadarProbe : IRadarProbe
{
    internal const int MaxOutputChars = 4 * 1024 * 1024;
    static readonly string Script = ReadScript();
    static string ReadScript()
    {
        using var stream = typeof(WindowsRadarProbe).Assembly.GetManifestResourceStream("TokuControl.SecurityRadarProbe.ps1")!;
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    internal static ProcessStartInfo StartInfo(RadarSource source)
    {
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        var command = "$source = '" + source + "'\n" + Script;
        // ArgumentList quotes the fixed command; no shell or user text is interpolated.
        // Leave space for worst-case Windows argument quoting, including CRLF checkouts.
        if (command.Length > 15000) throw new InvalidDataException("Probe command exceeds Windows argument budget");
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command", command }) psi.ArgumentList.Add(argument);
        // Only OS module locations: user/working-directory modules must not shadow read cmdlets.
        psi.Environment["PSModulePath"] = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "Modules");
        psi.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return psi;
    }
    internal static async Task<string> BoundedReadAsync(StreamReader reader, int limit, CancellationToken token)
    {
        var text = new StringBuilder(); var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        { if (text.Length + count > limit) throw new InvalidDataException("OutputLimit"); text.Append(buffer, 0, count); }
        return text.ToString();
    }
    public async Task<RadarRead> ReadAsync(RadarSource source, CancellationToken cancellation)
    {
        if (cancellation.IsCancellationRequested) return new(source, RadarCoverage.Cancelled, DateTimeOffset.Now, "ユーザーが取得を中止しました。", []);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation); timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var process = new Process { StartInfo = StartInfo(source) };
        try
        {
            if (!process.Start()) throw new IOException("StartFailed");
            var stdout = BoundedReadAsync(process.StandardOutput, MaxOutputChars, timeout.Token);
            var stderr = BoundedReadAsync(process.StandardError, 65536, timeout.Token);
            // A fault cancels both readers and the process wait so a full pipe cannot hang the scan.
            _ = stdout.ContinueWith(_ => timeout.Cancel(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            _ = stderr.ContinueWith(_ => timeout.Cancel(), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            var exit = process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(stdout, stderr, exit);
            if (process.ExitCode != 0) return new(source, RadarCoverage.Unavailable, DateTimeOffset.Now, "PowerShellが取得を完了できませんでした (exit " + process.ExitCode + ")。権限やOSポリシーは変更していません。", []);
            return Parse(source, await stdout);
        }
        catch (OperationCanceledException)
        { return new(source, cancellation.IsCancellationRequested ? RadarCoverage.Cancelled : RadarCoverage.Unavailable, DateTimeOffset.Now, cancellation.IsCancellationRequested ? "ユーザーが取得を中止しました。" : "取得タイムアウト (20秒)。", []); }
        catch (Exception ex)
        { return new(source, RadarCoverage.Unavailable, DateTimeOffset.Now, "取得失敗: " + ex.GetType().Name + "。権限の追加や設定変更はしていません。", []); }
        finally
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
        }
    }
    internal static RadarRead Parse(RadarSource source, string json)
    {
        if (json.Length > MaxOutputChars) throw new InvalidDataException("OutputLimit");
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        var coverageText = root.GetProperty("State").GetString();
        if (!Enum.GetNames<RadarCoverage>().Contains(coverageText) || !Enum.TryParse<RadarCoverage>(coverageText, out var coverage)) throw new InvalidDataException("Coverage");
        var time = root.GetProperty("CapturedAt").GetDateTimeOffset();
        var note = root.GetProperty("Note").GetString() ?? "";
        var rows = root.GetProperty("Rows").EnumerateArray().Select(r => new RadarRow(r.GetProperty("Key").GetString()!, r.GetProperty("Label").GetString()!, r.GetProperty("Value").GetString()!)).ToArray();
        if (rows.Length > 2000 || rows.Any(r => string.IsNullOrEmpty(r.Key) || r.Label == null || r.Value == null) || rows.Select(r => r.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rows.Length) throw new InvalidDataException("Rows");
        return new(source, coverage, time, note, rows);
    }
}
