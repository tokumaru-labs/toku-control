using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace TokuControl;

internal sealed class IntelWindow : RetroWindow
{
    enum View { Stream, Cache, Saved }
    sealed class Request : IDisposable
    {
        public readonly CancellationTokenSource Stop = new(TimeSpan.FromSeconds(40));
        public Task Task = Task.CompletedTask;
        public void Dispose() => Stop.Dispose();
    }
    readonly IIntelReader reader;
    internal readonly IntelLibrary Library;
    readonly bool demo;
    internal readonly CheckBox Online = Check("オンライン取得");
    internal readonly Dictionary<IntelSource, CheckBox> Choices = [];
    readonly Dictionary<IntelSource, TextBlock> states = [];
    readonly Dictionary<IntelSource, Request> active = [];
    readonly Dictionary<IntelSource, DateTimeOffset> attempted = [];
    readonly Dictionary<IntelSource, string> lastResult = [];
    readonly Dictionary<IntelSource, Queue<IntelArticle>> pending = [];
    readonly List<IntelArticle> shown = [];
    readonly HashSet<string> played = [];
    readonly StackPanel rows = new();
    readonly TextBlock bar = Retro.Label("", Retro.Cyan), headline = Retro.Label("", Retro.Green, 16), evidence = Retro.Label("", Retro.Text, 13);
    static readonly ControlTemplate ArticleTemplate = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Border x:Name="edge" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="5,2" Background="{TemplateBinding Background}">
            <ContentPresenter HorizontalAlignment="Stretch"/></Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="edge" Property="BorderBrush" Value="#DBCE49"/></Trigger>
            <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="edge" Property="Background" Value="#142045"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    internal readonly Button Pause, Refresh, Cache, Saved, Stream, Bookmark, Original, Discussion;
    CancellationTokenSource? playbackClock;
    readonly IntelSource[] sources = Enum.GetValues<IntelSource>();
    View view = View.Stream;
    internal bool Paused { get; private set; }
    bool closed;
    int rotation;
    IntelArticle? selected;
    internal IReadOnlyList<IntelArticle> Shown => shown;
    internal IntelArticle? SelectedArticle => selected;
    internal Button? FirstArticleButton => rows.Children.OfType<Button>().FirstOrDefault();
    internal string SourceStatus(IntelSource source) => states[source].Text;
    internal Task WhenIdle() => Task.WhenAll(active.Values.Select(w => w.Task).ToArray());
    internal static CheckBox Check(string name) => new() { Content = name, Foreground = Retro.Text, Margin = new Thickness(5, 4, 8, 4), VerticalContentAlignment = VerticalAlignment.Center };

    public IntelWindow(IIntelReader reader, IntelLibrary library, bool demo = false) : base("intel", "TOKU INTEL STREAM / 情報収集", 840, 640, true)
    {
        this.reader = reader; Library = library; this.demo = demo;
        MinWidth = 560; MinHeight = 440; Width = Math.Min(840, SystemParameters.WorkArea.Width); Height = Math.Min(640, SystemParameters.WorkArea.Height);
        ShowInTaskbar = true; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var selectors = new WrapPanel(); selectors.Children.Add(Online);
        foreach (var s in sources)
        {
            var box = Choices[s] = Check(IntelFeeds.Name(s)); pending[s] = new(); selectors.Children.Add(box);
            box.Checked += (_, _) => ChangeSource(s); box.Unchecked += (_, _) => ChangeSource(s);
        }
        var sourceStatus = new WrapPanel();
        foreach (var s in sources) { states[s] = Retro.Label("", Retro.Dim, 11); sourceStatus.Children.Add(states[s]); State(s, "OFF"); }
        var header = Retro.Col(Retro.Label(demo ? "PREVIEW / 架空記事・通信なし" : "INTEL STREAM / 外部ニュース", demo ? Retro.Amber : Retro.Green, 16),
            Retro.Label("情報源を選び「オンライン取得」をON。公開配信のみ取得し、このPCの診断情報は送りません。", Retro.Text),
            selectors, sourceStatus);
        header.Margin = new Thickness(8, 4, 8, 0); DockPanel.SetDock(header, Dock.Top); Layout.Children.Add(header);
        Stream = Retro.Button("STREAM", "選択した配信の流れる見出し", () => Switch(View.Stream));
        Cache = Retro.Button("CACHE / 履歴", "取得済みの見出しをオフラインで読む", () => Switch(View.Cache));
        Saved = Retro.Button("★ あとで読む", "保存した記事をオフラインで読む", () => Switch(View.Saved));
        Pause = Retro.Button("Ⅱ 一時停止", "取得は継続し、見出しの流れだけ停止・再開", () => { Paused = !Paused; if (!Paused) Advance(); Controls(); });
        Refresh = Retro.Button("更新", "選んだ配信だけ再取得（最短1分間隔）", () => { foreach (var s in sources.Where(Allowed)) Start(s, true); });
        var toolbar = new WrapPanel { Margin = new Thickness(6, 3, 6, 0) };
        foreach (var b in new[] { Stream, Cache, Saved, Pause, Refresh }) toolbar.Children.Add(b);
        DockPanel.SetDock(toolbar, Dock.Top); Layout.Children.Add(toolbar);
        var top = new Border { Child = bar, Margin = new Thickness(8, 2, 8, 2) }; DockPanel.SetDock(top, Dock.Top); Layout.Children.Add(top);

        var body = new Grid { Margin = new Thickness(7, 2, 7, 6) };
        body.RowDefinitions.Add(new() { Height = new GridLength(1.3, GridUnitType.Star) });
        body.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        body.Children.Add(new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Original = Retro.Button("原文を開く ↗", "選んだHTTPSリンクを既定ブラウザーで開く", () => Open(selected?.Url));
        Discussion = Retro.Button("HNの議論 ↗", "Hacker Newsの投稿をブラウザーで開く", () => Open(selected?.Discussion));
        Bookmark = Retro.Button("☆ 保存", "この見出しとリンクをあとで読む", () =>
        {
            if (selected == null) return; Library.ToggleSaved(selected);
            if (view == View.Saved && !Library.Saved.Any(a => a.Key == selected.Key)) selected = null;
            DisplayRows(); Detail(); Controls();
        });
        var detail = new DockPanel { Background = Retro.Panel, Margin = new Thickness(0, 5, 0, 0) };
        var actions = Retro.Row(Original, Discussion, Bookmark); DockPanel.SetDock(actions, Dock.Bottom); detail.Children.Add(actions);
        detail.Children.Add(new ScrollViewer { Content = Retro.Col(headline, evidence), VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetRow(detail, 1); body.Children.Add(detail); Layout.Children.Add(body);
        Online.Checked += (_, _) => { Switch(View.Stream); foreach (var s in sources.Where(Allowed)) Start(s, true); StartClock(); Controls(); };
        Online.Unchecked += (_, _) =>
        {
            StopClock(); foreach (var s in sources) { Cancel(s); ClearStream(s); State(s, "OFF"); }
            if (view == View.Stream) selected = null;
            DisplayRows(); Detail(); Controls();
        };
        Closed += (_, _) => { closed = true; StopClock(); foreach (var s in sources) Cancel(s); };
        DisplayRows(); Detail(); Controls();
    }
    void StartClock()
    {
        StopClock();
        playbackClock = new(); _ = RunClock(playbackClock);
    }
    void StopClock() { var previous = playbackClock; playbackClock = null; previous?.Cancel(); }
    async Task RunClock(CancellationTokenSource lifetime)
    {
        var token = lifetime.Token;
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1600));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                // Use the normal UI queue; background-priority dispatcher timers can stall on a busy desktop.
                await Dispatcher.InvokeAsync(() =>
                {
                    if (closed || playbackClock != lifetime || token.IsCancellationRequested) return;
                    foreach (var s in sources.Where(Allowed)) Start(s, false);
                    if (!Paused && view == View.Stream) Advance();
                }, DispatcherPriority.Normal, token).Task.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested || Dispatcher.HasShutdownStarted) { }
        finally { lifetime.Dispose(); }
    }
    bool Allowed(IntelSource s) => !closed && Online.IsChecked == true && Choices[s].IsChecked == true;
    void Cancel(IntelSource s) { if (active.Remove(s, out var req)) req.Stop.Cancel(); }
    void ClearStream(IntelSource s)
    {
        pending[s].Clear(); shown.RemoveAll(a => a.Source == s); played.RemoveWhere(k => k.StartsWith(s + "|", StringComparison.Ordinal));
        if (view == View.Stream && selected?.Source == s) selected = null;
    }
    void State(IntelSource s, string message) => states[s].Text = IntelFeeds.Short(s) + ": " + message;
    void ChangeSource(IntelSource s)
    {
        if (Allowed(s)) Start(s, true);
        else { Cancel(s); ClearStream(s); State(s, Online.IsChecked == true ? "対象外" : "OFF"); }
        DisplayRows(); Detail(); Controls();
    }
    void Start(IntelSource s, bool manual)
    {
        if (!Allowed(s) || active.ContainsKey(s)) return;
        var now = DateTimeOffset.UtcNow;
        if (attempted.TryGetValue(s, out var last) && now - last < TimeSpan.FromMinutes(manual ? 1 : 15))
        {
            if (manual) { State(s, lastResult.GetValueOrDefault(s, "中止") + " / 手動更新は " + last.AddMinutes(1).ToLocalTime().ToString("HH:mm:ss") + " 以降"); QueueSnapshot(s); }
            return;
        }
        attempted[s] = now; var req = new Request(); active[s] = req;
        State(s, "取得中…"); req.Task = Fetch(s, req);
    }
    async Task Fetch(IntelSource s, Request req)
    {
        bool Current() => Allowed(s) && active.TryGetValue(s, out var current) && current == req;
        try
        {
            var batch = await reader.ReadAsync(s, req.Stop.Token);
            req.Stop.Token.ThrowIfCancellationRequested();
            if (!Current()) return;
            Library.Store(s, batch);
            State(s, lastResult[s] = $"{batch.Items.Length}件 {DateTime.Now:HH:mm} {batch.Warning}"); QueueSnapshot(s);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            if (!Current()) return;
            var reason = ex switch { OperationCanceledException => "タイムアウト", HttpRequestException { StatusCode: { } code } => "HTTP " + (int)code,
                HttpRequestException => "接続不可", _ => "形式・読取エラー" };
            State(s, lastResult[s] = "取得失敗: " + reason + (Library.Feeds.TryGetValue(s, out var snap) ? " / 前回 " + snap.Fetched.ToLocalTime().ToString("MM/dd HH:mm") : " / 履歴なし"));
            QueueSnapshot(s);
        }
        finally
        {
            if (active.TryGetValue(s, out var current) && current == req) active.Remove(s);
            req.Dispose();
            if (!closed) { if (view != View.Stream) DisplayRows(); Controls(); }
        }
    }
    void QueueSnapshot(IntelSource s)
    {
        if (!Allowed(s) || !Library.Feeds.TryGetValue(s, out var snap)) return;
        pending[s].Clear();
        var current = snap.Articles.Select(a => a.Key).ToHashSet();
        played.RemoveWhere(k => k.StartsWith(s + "|", StringComparison.Ordinal) && !current.Contains(k));
        foreach (var a in snap.Articles.Where(a => !played.Contains(a.Key))) pending[s].Enqueue(a);
        if (!Paused && view == View.Stream && shown.Count == 0) Advance();
    }
    internal void Advance()
    {
        if (closed || Online.IsChecked != true || Paused || view != View.Stream) return;
        for (int i = 0; i < sources.Length; i++)
        {
            var s = sources[rotation++ % sources.Length];
            if (!Allowed(s) || !pending[s].TryDequeue(out var a)) continue;
            played.Add(a.Key); shown.RemoveAll(old => old.Key == a.Key); shown.Insert(0, a);
            if (shown.Count > 36) shown.RemoveAt(shown.Count - 1);
            if (rows.Children.Count > 0 && rows.Children[0] is TextBlock) rows.Children.Clear();
            var previous = rows.Children.OfType<Button>().FirstOrDefault(b => (string?)b.Tag == a.Key);
            if (previous != null) rows.Children.Remove(previous);
            rows.Children.Insert(0, ArticleButton(a)); if (rows.Children.Count > 36) rows.Children.RemoveAt(rows.Children.Count - 1);
            Controls(); return;
        }
    }
    void Switch(View choice) { view = choice; selected = null; Paused = choice != View.Stream; DisplayRows(); Detail(); Controls(); }
    Button ArticleButton(IntelArticle a)
    {
        var b = Retro.Button(a.Title, "止めて読む / " + a.Url, () => SelectArticle(a));
        var color = a.Source switch { IntelSource.JpCert => Retro.Green, IntelSource.HuggingFace => Retro.Brush("#C7ADFF"), _ => Retro.Amber };
        b.Content = Retro.Col(Retro.Label($"[{IntelFeeds.Short(a.Source)}]  {a.Title}", color, 13),
            Retro.Label($"{a.Topic}   /   {IntelFeeds.Name(a.Source)}   /   {Stamp(a)}", Retro.Text, 11));
        b.Tag = a.Key; b.Template = ArticleTemplate; b.Margin = new Thickness(2, 1, 2, 1);
        b.BorderBrush = a.Key == selected?.Key ? Retro.Cyan : Retro.Brush("#24354F");
        return b;
    }
    void DisplayRows()
    {
        rows.Children.Clear();
        IEnumerable<IntelArticle> articles = view switch { View.Stream => shown, View.Saved => Library.Saved,
            _ => Library.Feeds.Values.SelectMany(s => s.Articles).OrderByDescending(a => a.Date) };
        foreach (var a in articles) rows.Children.Add(ArticleButton(a));
        if (rows.Children.Count == 0) rows.Children.Add(Retro.Label(view == View.Stream
            ? Online.IsChecked == true ? "選んだ配信の見出しを待っています。取得状況は上部に表示されます。" : "INTEL INPUT OFF / 情報源とオンライン取得をチェック。以前の記事は CACHE から。"
            : view == View.Saved ? "保存した記事はまだありません。" : "この端末に取得履歴はまだありません。", Retro.Dim, 13));
    }
    static string Stamp(IntelArticle a) => IntelFeeds.DateKind(a.Source) + ": " + (a.Date?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz") ?? "日時不明");
    internal void SelectArticle(IntelArticle a)
    {
        selected = a; Paused = true; Detail(); Controls();
        foreach (var b in rows.Children.OfType<Button>()) b.BorderBrush = (string?)b.Tag == a.Key ? Retro.Cyan : Retro.Brush("#24354F");
    }
    void Detail()
    {
        var a = selected; headline.Text = a?.Title ?? "行を選ぶと停止し、記事の出典を確認できます。";
        Original.IsEnabled = !demo && a != null; Discussion.IsEnabled = !demo; Discussion.Visibility = a?.Discussion != null && a.Discussion != a.Url ? Visibility.Visible : Visibility.Collapsed;
        Original.Content = a != null && a.Url == a.Discussion ? "HN投稿を開く ↗" : "原文を開く ↗";
        Bookmark.IsEnabled = a != null; Bookmark.Content = a != null && Library.Saved.Any(b => b.Key == a.Key) ? "★ 保存を解除" : "☆ あとで読む";
        string note = a?.Source switch { IntelSource.HackerNews => "コミュニティで話題の投稿。投稿日時は原著の公開日時とは限りません。",
            IntelSource.JpCert => "注意喚起・週報。該当製品とバージョンは原文で確認。この記事だけでPCへの影響は判断できません。",
            _ => "モデル・研究・開発についてのブログ。作者による発表やコミュニティ投稿を含みます。" };
        evidence.Text = a == null ? "外部記事の紹介です。このPCの診断結果ではありません。" :
            $"{IntelFeeds.Name(a.Source)}   /   {Stamp(a)}\n{a.Topic}（見出しによる分類）   /   {note}\nリンク先: {a.Url}";
    }
    void Controls()
    {
        bool on = Online.IsChecked == true;
        Stream.BorderBrush = view == View.Stream ? Retro.Cyan : Retro.Border;
        Cache.BorderBrush = view == View.Cache ? Retro.Cyan : Retro.Border;
        Saved.BorderBrush = view == View.Saved ? Retro.Cyan : Retro.Border;
        Refresh.IsEnabled = on && sources.Any(Allowed); Pause.IsEnabled = on && view == View.Stream;
        Pause.Content = Paused ? "▶ 流れを再開" : "Ⅱ 一時停止";
        bar.Text = view switch
        {
            View.Cache => "CACHE / 最後に取得できた見出し · " + string.Join("  ", Library.Feeds.Select(f => IntelFeeds.Short(f.Key) + " " + f.Value.Fetched.ToLocalTime().ToString("MM/dd HH:mm"))),
            View.Saved => $"READ LATER / {Library.Saved.Count} 件 · 保存するのは見出しとリンクのみ",
            _ => !on ? "OFFLINE / このモードからの通信なし" : !sources.Any(Allowed) ? "情報源を1つ以上チェックしてください" : $"{(Paused ? "PAUSED" : "STREAM")} / 表示 {shown.Count}件 · 待機 {pending.Values.Sum(q => q.Count)}件"
        };
        Footer.Text = Library.Warning ?? (demo ? "PREVIEW / 通信なし" : "外部ニュース / 選択中のみ15分ごとに取得 · 端末ログ送信なし");
    }
    void Open(string? link)
    {
        if (demo || IntelFeeds.WebLink(link) is not { } uri) return;
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { Footer.Text = "ブラウザーで開けません / " + ex.GetType().Name; }
    }
}

public sealed partial class ConsoleWindow
{
    IntelWindow? intelWindow;
    internal void OpenIntel() => SelectSection("intel");
    void ShowIntelStream(IIntelReader? checkReader = null, bool liveCheck = false)
    {
        if (intelWindow == null)
        {
            var library = new IntelLibrary(Path.Combine(store.DirectoryPath, preview ? "intel-preview" : "intel")); library.Load();
            intelWindow = new(checkReader ?? (preview ? new IntelPreviewReader() : new IntelFeeds()), library, preview && !liveCheck);
            intelWindow.Apply(settings); intelWindow.Closed += (_, _) => intelWindow = null; intelWindow.Show();
        }
        if (intelWindow.WindowState == WindowState.Minimized) intelWindow.WindowState = WindowState.Normal;
        WindowPlacement.OverConsole(this, intelWindow); intelWindow.Activate();
    }
}

internal sealed class IntelPreviewReader : IIntelReader
{
    public Task<IntelBatch> ReadAsync(IntelSource s, CancellationToken token) => Task.FromResult(new IntelBatch([
        new(s, "DEMO / " + IntelFeeds.Name(s) + " の架空の見出し", "https://example.org/?demo=" + s, DateTimeOffset.UnixEpoch)]));
}
