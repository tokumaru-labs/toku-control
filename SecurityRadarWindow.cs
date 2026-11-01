using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace TokuControl;

internal sealed class SecurityRadarWindow : RetroWindow
{
    readonly RadarSession session;
    readonly bool demo;
    readonly StackPanel cards = new();
    readonly TextBlock status = Retro.Label("NOT SCANNED / 調べる項目と説明を選べます", Retro.Cyan, 12);
    readonly TextBlock referenceStatus = Retro.Label("比較基準なし / アプリ終了までのメモリ内だけで保持", Retro.Dim, 11);
    readonly TextBox detail = new() { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Retro.Background, Foreground = Retro.Text, BorderThickness = new Thickness(0), Padding = new Thickness(12), FontSize = 13 };
    internal readonly Button RefreshButton, CancelButton, ReferenceButton;
    CancellationTokenSource? cancellation;
    bool closed, sourceView;
    int selected;
    internal Task? Reading { get; private set; }
    internal RadarSession Session => session;
    internal string DetailText => detail.Text;
    internal int CardCount => cards.Children.Count;
    public SecurityRadarWindow(RadarSession session, bool demo = false) : base("security-radar", "SECURITY RADAR / ATTACK MAP", 1100, 760, true)
    {
        this.session = session; this.demo = demo;
        TextBlock.SetLineHeight(detail, 19);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = Math.Min(1100, SystemParameters.WorkArea.Width); Height = Math.Min(760, SystemParameters.WorkArea.Height);
        ShowInTaskbar = true;
        RefreshButton = Retro.Button("READ / REFRESH", "このPCの状態を読み取る。設定変更・昇格・送信は行いません", () => Reading = RefreshAsync());
        CancelButton = Retro.Button("CANCEL", "進行中の読み取りを中止", () => cancellation?.Cancel());
        ReferenceButton = Retro.Button("SET REFERENCE", "取得済みの永続化4項目を比較基準に固定。安全だと承認する操作ではありません", () => { if (session.SetReference()) Render(); });
        var header = Retro.Col(Retro.Label(demo ? "DEMO / 架空のデータ・このPCの診断ではありません" : "LOCAL INSPECTION / 読み取り専用・手動更新", demo ? Retro.Amber : Retro.Green, 15),
            Retro.Label("保護状態 → 永続化の変化 → 認証 → 通信   |   攻撃手法との対応は調査の手がかりです", Retro.Text, 12),
            Retro.Row(RefreshButton, CancelButton, ReferenceButton), status, referenceStatus);
        header.Margin = new Thickness(10, 8, 10, 6); DockPanel.SetDock(header, Dock.Top); Layout.Children.Add(header);
        var grid = new Grid { Margin = new Thickness(8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new DockPanel();
        var tabs = Retro.Row(Retro.Button("ATTACK MAP", "手口から観測項目を逆引き", () => SetView(false)), Retro.Button("SOURCES", "取得元ごとの状態・範囲を表示", () => SetView(true)));
        DockPanel.SetDock(tabs, Dock.Top); left.Children.Add(tabs);
        left.Children.Add(new ScrollViewer { Content = cards, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }); grid.Children.Add(left);
        var evidence = Retro.Box("EVIDENCE / 根拠・限界", detail); Grid.SetColumn(evidence, 1); grid.Children.Add(evidence); Layout.Children.Add(grid);
        Footer.Text = "NO AUTO-REMEDIATION / NO UPLOAD   •   取得不可は安全判定に含めません";
        session.Changed += Render;
        Closed += (_, _) => { closed = true; session.Changed -= Render; cancellation?.Cancel(); };
        Render();
    }
    internal void SetView(bool sources) { sourceView = sources; selected = 0; Render(); }
    internal void SelectCard(int index) { selected = index; RenderDetails(); }
    internal async Task RefreshAsync()
    {
        if (session.Busy) return;
        cancellation?.Dispose(); cancellation = new();
        var read = session.RefreshAsync(cancellation.Token);
        await read;
        if (!closed) Render();
    }
    static Brush Color(RadarSignal signal) => signal switch { RadarSignal.Review => Retro.Amber, RadarSignal.Observed => Retro.Cyan, _ => Retro.Dim };
    RadarAssessment Assessment(RadarSource source) => session.Current.TryGetValue(source, out var read)
        ? RadarAnalysis.Assess(read, session.Reference.GetValueOrDefault(source)) : new(RadarSignal.Unknown, "未取得", "READ / REFRESH で読み取れます。");
    void Render()
    {
        RefreshButton.IsEnabled = !session.Busy; CancelButton.IsEnabled = session.Busy;
        ReferenceButton.IsEnabled = !session.Busy && RadarKnowledge.Persistence.All(s => session.Current.TryGetValue(s, out var r) && r.Coverage == RadarCoverage.Complete);
        var missing = session.Current.Values.Count(r => r.Coverage != RadarCoverage.Complete);
        status.Text = session.Busy ? $"READING / {session.Current.Count} / 9 sources   ·   各取得は最大20秒" : session.Current.Count == 0 ? "NOT SCANNED / 手動で読み取りを開始" : $"SNAPSHOT / {session.Current.Count} sources   ·   未完了 {missing}   ·   表示は取得時点の状態";
        referenceStatus.Text = session.Reference.Count == 0 ? "比較基準なし / 初回一覧は不審判定しません。基準はメモリ内のみ。" : "比較基準: " + session.Reference.Values.Min(r => r.CapturedAt).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") + " / 再設定まで固定・アプリ終了で破棄";
        cards.Children.Clear();
        int count = sourceView ? 9 : RadarKnowledge.Techniques.Length;
        for (int i = 0; i < count; i++)
        {
            int index = i;
            var sources = sourceView ? new[] { (RadarSource)i } : RadarKnowledge.Techniques[i].Sources;
            var states = sources.Select(Assessment).ToArray();
            var signal = states.Any(s => s.Signal == RadarSignal.Unknown) ? RadarSignal.Unknown : states.Any(s => s.Signal == RadarSignal.Review) ? RadarSignal.Review : states.Any(s => s.Signal == RadarSignal.NotCompared) ? RadarSignal.NotCompared : RadarSignal.Observed;
            string title = sourceView ? RadarKnowledge.Title((RadarSource)i) : RadarKnowledge.Techniques[i].Id + " / " + RadarKnowledge.Techniques[i].Title;
            var b = Retro.Button(title, "取得根拠と確認範囲を見る", () => SelectCard(index));
            b.Content = Retro.Col(Retro.Label(title, Color(signal), 12), Retro.Label(RadarKnowledge.Signal(signal), Color(signal), 10));
            b.HorizontalContentAlignment = HorizontalAlignment.Stretch; b.Margin = new Thickness(2, 3, 5, 3);
            cards.Children.Add(b);
        }
        RenderDetails();
    }
    void RenderDetails()
    {
        var sources = sourceView ? new[] { (RadarSource)selected } : RadarKnowledge.Techniques[selected].Sources;
        var text = new System.Text.StringBuilder();
        if (demo) text.AppendLine("DEMO / 架空の例\n");
        if (!sourceView)
        {
            var technique = RadarKnowledge.Techniques[selected];
            text.AppendLine(technique.Id + " / " + technique.Title).AppendLine(technique.Explanation).AppendLine("公式資料: " + technique.Url).AppendLine();
        }
        foreach (var source in sources)
        {
            var assessment = Assessment(source);
            text.AppendLine("━━ " + RadarKnowledge.Title(source) + " ━━").AppendLine(RadarKnowledge.Signal(assessment.Signal)).AppendLine(assessment.Summary).AppendLine(assessment.Explanation);
            if (session.Current.TryGetValue(source, out var read))
            {
                text.AppendLine("取得: " + read.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz")).AppendLine(RadarKnowledge.Coverage(read.Coverage)).AppendLine(read.Note).AppendLine();
                if (session.Reference.TryGetValue(source, out var reference))
                    foreach (var change in RadarAnalysis.Compare(reference, read)) text.AppendLine(change.Kind + " / " + change.Name).AppendLine("  BEFORE: " + change.Before).AppendLine("  AFTER: " + change.After).AppendLine();
                foreach (var row in read.Rows) text.AppendLine(row.Label).AppendLine("  " + row.Value).AppendLine();
            }
            text.AppendLine();
        }
        detail.Text = text.ToString();
    }
}

public sealed partial class ConsoleWindow
{
    SecurityRadarWindow? radarWindow;
    RadarSession? radarSession;
    void ShowSecurityRadar()
    {
        if (radarWindow != null) { if (radarWindow.WindowState == WindowState.Minimized) radarWindow.WindowState = WindowState.Normal; WindowPlacement.OverConsole(this, radarWindow); radarWindow.Activate(); return; }
        radarSession ??= new(preview ? new DemoRadarProbe() : new WindowsRadarProbe());
        radarWindow = new(radarSession, preview);
        radarWindow.Apply(settings);
        radarWindow.Closed += (_, _) => radarWindow = null;
        radarWindow.Show(); WindowPlacement.OverConsole(this, radarWindow); radarWindow.Activate();
    }
}

internal sealed class DemoRadarProbe : IRadarProbe
{
    public Task<RadarRead> ReadAsync(RadarSource source, CancellationToken cancellation)
    {
        var rows = source switch
        {
            RadarSource.Defender => new[] { "AMServiceEnabled", "AntivirusEnabled", "RealTimeProtectionEnabled", "BehaviorMonitorEnabled", "IsTamperProtected" }.Select(k => new RadarRow(k, k, "True")).Append(new("AMRunningMode", "AMRunningMode", "Normal")).ToArray(),
            RadarSource.Firewall => new[] { "Domain", "Private", "Public" }.Select(k => new RadarRow(k, k, "True")).ToArray(),
            RadarSource.Authentication => [],
            RadarSource.Tcp => [new RadarRow("1", "Example browser / PID 100 / Established", "[192.0.2.10]:51000 -> [203.0.113.20]:443")],
            RadarSource.Udp => [new RadarRow("1", "Example local service / PID 101", "[127.0.0.1]:5353 / UDP endpoint")],
            _ => [new RadarRow("demo", "Example startup entry", "C:\\Example\\Sample.exe / illustrative only")]
        };
        return Task.FromResult(new RadarRead(source, cancellation.IsCancellationRequested ? RadarCoverage.Cancelled : RadarCoverage.Complete, DateTimeOffset.Now, "DEMO / 架空のデータ", rows));
    }
}
