using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace TokuControl;

internal sealed class UpdateWindow : RetroWindow
{
    readonly UpdateSession session;
    readonly TextBlock status = Retro.Label("", Retro.Cyan, 18);
    readonly TextBlock detail = Retro.Label("", Retro.Text, 12);
    readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 12, Margin = new Thickness(3, 8, 3, 8) };
    readonly Button check, yes, later;
    public UpdateWindow(UpdateSession session, Settings settings, Action save, Action beforeRestart, Action shutdown, string[] restartArgs)
        : base("updates", "TOKU CONTROL / SOFTWARE UPDATE", 560, 365)
    {
        this.session = session;
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var auto = new CheckBox { Content = "起動時に更新を確認 / Check on startup", IsChecked = settings.CheckUpdatesOnStartup, Foreground = Retro.Text, Margin = new Thickness(3, 10, 3, 4) };
        auto.Click += (_, _) => { settings.CheckUpdatesOnStartup = auto.IsChecked == true; save(); };
        check = Retro.Button("CHECK NOW", "最新版を確認", () => _ = session.CheckAsync());
        yes = Retro.Button("YES / UPDATE", "更新をダウンロードして再起動", () => _ = session.InstallAsync(true, beforeRestart, shutdown, restartArgs));
        later = Retro.Button("LATER", "今回は更新せず閉じる", Close);
        var notes = Retro.Button("RELEASE NOTES", "GitHubのリリース情報をブラウザーで開く", () =>
        {
            try { using var process = Process.Start(new ProcessStartInfo(GithubUpdates.Repository + "/releases/latest") { UseShellExecute = true }); }
            catch { Footer.Text = "ブラウザーを開けませんでした。"; }
        });
        var content = Retro.Col(Retro.Label("CURRENT / v" + GithubUpdates.AppVersion, Retro.Dim, 11), status, detail, progress,
            Retro.Row(yes, later, check), auto,
            Retro.Label("確認先: GitHub / tokumaru-labs/toku-control\n「Yes」を押したときだけ取得・更新・再起動します。", Retro.Dim, 11), notes);
        content.Margin = new Thickness(14, 10, 14, 8); Layout.Children.Add(content);
        Footer.Text = "SETTINGS ARE KEPT / 設定は引き継がれます";
        session.Changed += Render;
        Closing += (_, e) =>
        {
            if (session.Phase == UpdatePhase.Installing && Owner?.IsVisible == true) { e.Cancel = true; return; }
            session.Cancel();
        };
        Closed += (_, _) => session.Changed -= Render;
        Apply(settings); Render();
    }
    void Render()
    {
        status.Text = session.Phase switch
        {
            UpdatePhase.Checking => "Checking for updates...",
            UpdatePhase.Available => "New version available",
            UpdatePhase.Current => "You're up to date",
            UpdatePhase.Downloading => $"Downloading... {session.Progress}%",
            UpdatePhase.Installing => "Updating / restarting...",
            UpdatePhase.Cancelled => "Update cancelled",
            UpdatePhase.Error => "Update could not complete",
            UpdatePhase.Unavailable => "Updates unavailable in this build",
            _ => "Check for updates"
        };
        detail.Text = session.Phase switch
        {
            UpdatePhase.Available => $"v{session.Offer!.Version} / {session.Offer.Bytes / 1000000d:0.0} MB\n今すぐ更新しますか？ 更新後に自動で再起動します。",
            UpdatePhase.Downloading => "ダウンロード・検証中です。Cancelで中止できます。",
            UpdatePhase.Installing => "更新の完了をお待ちください。",
            UpdatePhase.Current => "最新版を使用しています。",
            UpdatePhase.Cancelled => "現在のバージョンを続けて使えます。",
            UpdatePhase.Error => session.Message,
            UpdatePhase.Unavailable => "配布ZIPをすべて展開し、一番上のTokuControl.exeを起動してください。プレビューでは更新しません。",
            _ => "新しい正式版があるか確認します。"
        };
        yes.Visibility = session.Offer != null && !session.Busy ? Visibility.Visible : Visibility.Collapsed;
        check.IsEnabled = !session.Busy && session.Phase != UpdatePhase.Unavailable;
        later.IsEnabled = session.Phase != UpdatePhase.Installing;
        later.Content = session.Phase == UpdatePhase.Downloading ? "CANCEL" : "LATER";
        progress.Visibility = session.Phase is UpdatePhase.Downloading or UpdatePhase.Checking ? Visibility.Visible : Visibility.Collapsed;
        progress.IsIndeterminate = session.Phase == UpdatePhase.Checking; progress.Value = session.Progress;
    }
}

public sealed partial class ConsoleWindow
{
    UpdateSession updates = null!;
    UpdateWindow? updateWindow;
    bool automaticUpdateCheck;
    void InitializeUpdates(bool enabled)
    {
        updates = new(enabled && !preview ? new GithubUpdates() : null);
        updates.Changed += () =>
        {
            if (shuttingDown) return;
            if (updates.Phase == UpdatePhase.Available && automaticUpdateCheck) ShowUpdates();
            if (updates.Phase == UpdatePhase.Error) Log("UPDATE", updates.Message);
        };
        Loaded += async (_, _) =>
        {
            if (!enabled || preview || !settings.CheckUpdatesOnStartup) return;
            await Task.Delay(2000);
            if (shuttingDown || !settings.CheckUpdatesOnStartup) return;
            automaticUpdateCheck = true;
            try { await updates.CheckAsync(); }
            finally { automaticUpdateCheck = false; }
        };
        Closed += (_, _) => { updates.Stop(); updateWindow?.Close(); };
    }
    void ShowUpdates()
    {
        if (updateWindow != null) { updateWindow.Activate(); return; }
        updateWindow = new UpdateWindow(updates, settings, Save, () =>
        {
            if (outputBusy || driveBusy) throw new InvalidOperationException("音声・ドライブの操作が完了してから、もう一度Yesを押してください。");
            settings.Windows["main"] = WindowPlacement.Read(this);
            foreach (var window in modules.Values) settings.Windows[window.Key] = WindowPlacement.Read(window);
            var error = store.Save(settings);
            if (error != null) throw new InvalidOperationException("設定を保存できないため更新を中止しました。");
        }, () => { updateWindow!.Owner = null; updateWindow.Close(); Close(); }, ["--data-dir", store.DirectoryPath]) { Owner = this };
        updateWindow.Closed += (_, _) => updateWindow = null;
        updateWindow.Show();
    }
}
