using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Microsoft.Win32;

namespace TokuControl;

internal sealed class AutorunscWindow : Window
{
    sealed record DisplayRow(string Kind, string Entry, string Location, string Profile, string Category,
        string Enabled, string Image, string Command);

    readonly bool preview;
    readonly Button scan, import, reference, cancel;
    readonly TextBlock status = Retro.Label("未取得", Retro.Cyan, 12);
    readonly TextBlock baselineLabel = Retro.Label("比較基準なし", Retro.Dim, 12);
    readonly DataGrid table = new()
    {
        IsReadOnly = true, AutoGenerateColumns = false, CanUserAddRows = false,
        CanUserDeleteRows = false, EnableColumnVirtualization = true, EnableRowVirtualization = true,
        HeadersVisibility = DataGridHeadersVisibility.Column
    };
    List<StartupItem>? latest, baseline;
    string lastRead = "";
    CancellationTokenSource? reading;
    bool closed;

    internal static void Open(Window owner, bool preview)
    {
        var window = owner.OwnedWindows.OfType<AutorunscWindow>().FirstOrDefault();
        if (window == null) new AutorunscWindow(preview) { Owner = owner }.Show();
        else { if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal; window.Activate(); }
    }

    internal AutorunscWindow(bool preview)
    {
        this.preview = preview;
        Title = "Autorunsc · Security Radar"; Width = 940; Height = 520; MinWidth = 510; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Retro.Background;
        var root = new DockPanel { Margin = new Thickness(10) };
        scan = Retro.Button("EXEを選んで取得", "Microsoft の Autorunsc 14.3 を通常権限で1回だけ実行", () => Choose(true));
        import = Retro.Button("CSV読込", "保存済みのローカルCSVを開く", () => Choose(false));
        reference = Retro.Button("比較基準にする", "表示中の取得結果をこのウィンドウの比較基準にする", SetReference);
        cancel = Retro.Button("中止", "実行中の取得を中止", () => reading?.Cancel());
        var buttons = new WrapPanel();
        foreach (var button in new[] { scan, import, reference, cancel }) buttons.Children.Add(button);
        var header = Retro.Col(Retro.Label("自動起動項目 / AUTORUNSC", Retro.Green, 16),
            Retro.Label(preview ? "プレビューではこの PC の取得・ファイル読込は無効です。" :
                "Microsoft から別途入手した EXE で1回取得、または保存済み CSV を読込。初回の利用条件はご自身で確認してください。", Retro.Text, 12),
            Retro.Label("同じPC・アカウント・取得条件の結果を比較してください。差分は登録内容の変化を示します。", Retro.Text, 12),
            buttons, baselineLabel, status);
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        foreach (var (label, field) in new[] { ("表示", "Kind"), ("項目", "Entry"), ("登録場所", "Location"),
            ("プロファイル", "Profile"), ("分類", "Category"), ("有効", "Enabled"), ("実行ファイル", "Image"), ("コマンド", "Command") })
            table.Columns.Add(new DataGridTextColumn { Header = label, Binding = new Binding(field), MaxWidth = 700 });
        root.Children.Add(table); Content = root;
        Closed += (_, _) => { closed = true; reading?.Cancel(); };
        Controls();
    }

    void Controls()
    {
        scan.IsEnabled = import.IsEnabled = !preview && reading == null;
        cancel.IsEnabled = reading != null;
        reference.IsEnabled = latest != null && reading == null;
    }

    internal bool CanCollect => scan.IsEnabled;
    internal string StatusText => status.Text;
    internal int DisplayedRows => table.Items.Count;

    async void Choose(bool collect)
    {
        if (preview || reading != null) return;
        var dialog = new OpenFileDialog
        {
            Title = collect ? "選んだ Autorunsc で1回取得" : "Autorunsc CSV を選択",
            Filter = collect ? "Autorunsc 14.3|autorunsc64.exe;autorunsc.exe" : "Autorunsc CSV|*.csv",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            AddToRecent = false, DereferenceLinks = false
        };
        if (dialog.ShowDialog(this) == true) await ReadFile(dialog.FileName, collect);
    }

    internal async Task ReadFile(string file, bool collect)
    {
        if (closed || preview || reading != null) return;
        using var cancelSource = new CancellationTokenSource();
        reading = cancelSource; Controls(); status.Text = "読み取り中…";
        try
        {
            var data = await Task.Run(() => AutorunscReader.Read(file, collect, cancelSource.Token));
            cancelSource.Token.ThrowIfCancellationRequested();
            if (closed) return;
            latest = data;
            lastRead = (collect ? "EXE 取得" : "CSV 読込") + $" {DateTime.Now:HH:mm:ss} · {data.Count} 件";
            Display();
        }
        catch (OperationCanceledException) { status.Text = "中止しました。前回の結果と基準を保持します。"; }
        catch (Exception ex) { status.Text = "取得失敗（前回の結果と基準を保持） " + ex.Message; }
        finally { reading = null; if (!closed) Controls(); }
    }

    internal void SetReference()
    {
        if (latest == null) return;
        baseline = latest;
        baselineLabel.Text = "比較基準 " + lastRead + "（この画面を閉じるまで）";
        Display();
    }

    void Display()
    {
        if (latest == null) return;
        static DisplayRow Row(string kind, StartupIdentity id, StartupValue value) =>
            new(kind, id.Entry, id.Location, id.Profile, id.Category, value.Enabled, value.Image, value.Command);
        if (baseline == null || ReferenceEquals(baseline, latest))
        {
            status.Text = lastRead + " · 取得範囲の一覧";
            table.ItemsSource = latest.Select(i => Row("一覧", i.Identity, i.Value)).ToList();
            return;
        }
        var changes = AutorunscSnapshot.Compare(baseline, latest);
        var rows = new List<DisplayRow>();
        foreach (var change in changes)
        {
            if (change.Before != null) rows.Add(Row(change.Kind == "変更" ? "変更前" : change.Kind, change.Identity, change.Before));
            if (change.After != null) rows.Add(Row(change.Kind == "変更" ? "変更後" : change.Kind, change.Identity, change.After));
        }
        status.Text = lastRead + $" · 基準との差分 {changes.Count} 件";
        table.ItemsSource = rows;
    }
}
