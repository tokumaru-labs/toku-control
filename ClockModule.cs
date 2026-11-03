using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokuControl;

public sealed partial class ConsoleWindow
{
    static readonly Lazy<BitmapSource[]> HackerScenes = new(() => LoadScenes("NemumaruHacker.png"));
    RadarSnapshotPanel? clockRadar;
    static BitmapSource[] LoadScenes(string filename)
    {
        var atlas = new BitmapImage(new Uri($"pack://application:,,,/TokuControl;component/Assets/{filename}"));
        int width = atlas.PixelWidth / 2, height = atlas.PixelHeight / 2;
        return Enumerable.Range(0, 4).Select(i =>
        {
            // Leave the one-pixel dividing line of the generated atlas outside each viewport.
            var frame = new CroppedBitmap(atlas, new Int32Rect(i % 2 * width + 2, i / 2 * height + 2, width - 4, height - 4));
            frame.Freeze(); return (BitmapSource)frame;
        }).ToArray();
    }
    TraceConsole traceConsole = null!;
    string? cancelledCommand;
    Grid clockOverview = null!;
    TraceDetails traceDetails = null!;
    Button detailToggle = null!;
    bool inspectingTrace;
    Button traceMotion = null!, startPomo = null!;
    NightMandala mandala = null!;
    ComboBox clockTheme = null!;
    MinuteWheel minuteWheel = null!;
    Image room = null!;
    TextBlock roomLabel = null!, remaining = null!;
    Button startRest = null!, resetRest = null!;
    ProgressBar timerBar = null!;
    int selectedMinutes = 25;

    RetroWindow BuildClockModule()
    {
        var window = new RetroWindow("clock", "CLOCK MODULE", 420, 212);
        var body = new Grid { Margin = new Thickness(5, 2, 5, 2) };
        body.ColumnDefinitions.Add(new() { Width = new GridLength(142) }); body.ColumnDefinitions.Add(new());
        room = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(room, BitmapScalingMode.NearestNeighbor);
        roomLabel = Retro.Label("", Retro.Cyan, 11); roomLabel.HorizontalAlignment = HorizontalAlignment.Center;
        var art = new DockPanel { Margin = new Thickness(0, 1, 4, 0) };
        clockTheme = new ComboBox { Height = 23, FontSize = 11, ToolTip = "時計の育成テーマ" };
        clockTheme.Items.Add("部屋"); clockTheme.Items.Add("TRACE 端末"); clockTheme.Items.Add("宵環曼荼羅");
        clockTheme.SelectedIndex = settings.ClockMandala ? 2 : settings.ClockTrace ? 1 : 0;
        AutomationProperties.SetName(clockTheme, "時計の育成テーマ");
        clockTheme.SelectionChanged += (_, _) =>
        {
            settings.ClockTrace = clockTheme.SelectedIndex == 1; settings.ClockMandala = clockTheme.SelectedIndex == 2;
            Save();
            RenderRest(window);
        };
        traceConsole = new TraceConsole(window);
        traceMotion = Retro.Button("Ⅱ", "端末のタイプ演出を停止", () =>
        {
            settings.ClockCodeMotion = !settings.ClockCodeMotion;
            Save();
            RenderRest(window);
        });
        traceMotion.Width = 26; traceMotion.Height = traceMotion.MinHeight = 23;
        traceMotion.Margin = new Thickness(2, 0, 0, 0); traceMotion.Padding = new Thickness(0);
        var themeRow = new DockPanel();
        DockPanel.SetDock(traceMotion, Dock.Right); themeRow.Children.Add(traceMotion); themeRow.Children.Add(clockTheme);
        DockPanel.SetDock(themeRow, Dock.Top); art.Children.Add(themeRow);
        DockPanel.SetDock(roomLabel, Dock.Bottom); art.Children.Add(roomLabel);
        var scene = new Grid(); scene.Children.Add(room);
        var snapshot = new RadarSnapshotPanel(preview, source => {
            ShowSecurityRadar();
            radarWindow!.ShowSource(source);
        });
        clockRadar = snapshot; snapshot.Bind(radarSession);
        window.Closed += (_, _) => { snapshot.Bind(null); if (clockRadar == snapshot) clockRadar = null; };
        scene.Children.Add(snapshot);
        mandala = new NightMandala(); scene.Children.Add(mandala);
        art.Children.Add(scene); body.Children.Add(art);

        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(controls, 1); body.Children.Add(controls);
        clock = new SegmentClock { Height = 44, Margin = new Thickness(2, 0, 2, 0) }; controls.Children.Add(clock);
        clockDate = Retro.Label("", Retro.Text, 10); clockDate.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(clockDate); controls.Children.Add(traceConsole);
        minuteWheel = new MinuteWheel { Value = selectedMinutes };
        minuteWheel.ValueChanged += (_, _) => selectedMinutes = (int)minuteWheel.Value;
        startRest = Retro.Button("START", "選んだ分数で無音タイマーを開始", () =>
        {
            if (rest.PendingSave) rest.RetrySave();
            else if (rest.Start(selectedMinutes)) { cancelledCommand = null; Log("INFO", $"silent timer set / {selectedMinutes} min"); }
            RenderRest(window);
        });
        resetRest = Retro.Button("RESET", "タイマーを中断して待機に戻る", () => { cancelledCommand = rest.Running && rest.Remaining > TimeSpan.Zero ? (rest.Focusing ? "focus.cancel" : "rest.cancel") : null; rest.Reset(); RenderRest(window); });
        var pin = Retro.Button("PIN", "時計の最前面固定を切り替える", () => { window.Topmost = !window.Topmost; SavePositions(); });
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        remaining = Retro.Label("", Retro.Green, 12); actions.Children.Add(remaining);
        actions.Children.Add(Retro.Row(startRest, resetRest, pin));
        var row = Retro.Row(minuteWheel, actions); row.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(row);
        timerBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 5, Foreground = Retro.Green, Background = Retro.Panel, Margin = new Thickness(4, 2, 4, 1) };
        AutomationProperties.SetName(timerBar, "休憩の進捗"); controls.Children.Add(timerBar);
        clockOverview = body; traceDetails = new TraceDetails();
        var pages = new Grid(); pages.Children.Add(body); pages.Children.Add(traceDetails);
        window.Layout.Children.Add(pages);
        detailToggle = Retro.Button("DETAIL", "休憩の実測グラフと操作履歴を表示", () => {
            inspectingTrace = !inspectingTrace; RenderRest(window);
        });
        detailToggle.MinHeight = 18; detailToggle.Padding = new Thickness(6, 0, 6, 0);
        detailToggle.Margin = new Thickness(0, 0, 2, 0);
        var footerBox = (Border)window.Footer.Parent;
        footerBox.Child = null;
        var footerRow = new DockPanel(); DockPanel.SetDock(detailToggle, Dock.Right);
        footerRow.Children.Add(detailToggle);
        startPomo = Retro.Button("20/5 ▶", "ポモドーロ開始：集中20分と休憩5分を繰り返す", () => {
            if (rest.StartPomodoro()) { cancelledCommand = null; RenderRest(window); }
        });
        startPomo.MinHeight = 18; startPomo.Padding = new Thickness(5,0,5,0); startPomo.Margin = new Thickness(0,0,2,0);
        AutomationProperties.SetName(startPomo, "ポモドーロ開始 集中20分／休憩5分");
        DockPanel.SetDock(startPomo, Dock.Right); footerRow.Children.Add(startPomo); footerRow.Children.Add(window.Footer); footerBox.Child = footerRow;
        RenderRest(window);
        return window;
    }
    void RenderRest(RetroWindow window)
    {
        var growth = rest.Growth;
        room.Source = HackerScenes.Value[growth.Scene];
        mandala.Visibility = settings.ClockMandala ? Visibility.Visible : Visibility.Collapsed;
        if (settings.ClockMandala) mandala.SetDays(growth.Days);
        room.Visibility = settings.ClockTrace || settings.ClockMandala ? Visibility.Collapsed : Visibility.Visible;
        clockRadar!.Visibility = settings.ClockTrace ? Visibility.Visible : Visibility.Collapsed;
        roomLabel.Text = settings.ClockMandala ? $"{growth.Days}日 · 本日{rest.TodayRounds}周" : settings.ClockTrace ? $"STAGE {growth.Scene + 1}/4 · {growth.Days}日" : $"{growth.SceneName} · DAY {growth.Days:000}";
        AutomationProperties.SetName(room, "ねむまる・ハッカー版 / " + roomLabel.Text);
        clockDate!.Visibility = settings.ClockTrace ? Visibility.Collapsed : Visibility.Visible;
        int seconds = (int)Math.Ceiling(rest.Remaining.TotalSeconds);
        string command = rest.Focusing ? "focus.start 20m" : rest.Running ? $"rest.start {rest.Minutes}m" :
            rest.Finished ? "rest.result" : cancelledCommand ?? "trace.status";
        string status = rest.Running ? $"LEFT {seconds / 60:00}:{seconds % 60:00}" : rest.PendingSave ? "SAVE PENDING" :
            rest.Finished ? "COMPLETE" : cancelledCommand != null ? "CANCELLED" : "READY";
        if (!settings.ClockTrace) inspectingTrace = false;
        detailToggle.Visibility = settings.ClockTrace ? Visibility.Visible : Visibility.Collapsed;
        detailToggle.Content = inspectingTrace ? "← CLOCK" : "DETAIL";
        string detailHint = inspectingTrace ? "時計へ戻る" : "休憩の実測グラフと操作履歴を表示";
        detailToggle.ToolTip = detailHint; AutomationProperties.SetName(detailToggle, detailHint);
        clockOverview.Visibility = inspectingTrace ? Visibility.Collapsed : Visibility.Visible;
        traceDetails.Visibility = inspectingTrace ? Visibility.Visible : Visibility.Collapsed;
        if (inspectingTrace) traceDetails.Refresh(rest);
        bool firstStart = rest.Running && rest.History.Runs.LastOrDefault()?.Number == 1 && rest.ElapsedSeconds == 0;
        traceConsole.Display(settings.ClockTrace, settings.ClockCodeMotion && firstStart, command, $"{status}  /  STAGE {growth.Scene + 1:00}");
        traceMotion.Visibility = settings.ClockTrace ? Visibility.Visible : Visibility.Collapsed;
        traceMotion.IsEnabled = SystemParameters.ClientAreaAnimation;
        traceMotion.Content = settings.ClockCodeMotion && traceMotion.IsEnabled ? "Ⅱ" : "▶";
        string motionHint = !traceMotion.IsEnabled ? "Windowsの設定で動きを停止中" :
            settings.ClockCodeMotion ? "端末のタイプ演出を停止" : "端末のタイプ演出を再開";
        traceMotion.ToolTip = motionHint; AutomationProperties.SetName(traceMotion, motionHint);
        remaining.Text = rest.Running ? $"{(rest.Focusing ? "集中" : rest.Cycling ? "休憩" : "残り")} {seconds / 60:00}:{seconds % 60:00}" : rest.Finished ? "休憩完了" : "休憩タイマー";
        timerBar.Value = rest.Running ? 1 - rest.Remaining.TotalMinutes / rest.Minutes : rest.Finished ? 1 : 0;
        minuteWheel.IsEnabled = !rest.Running && !rest.PendingSave;
        startPomo.IsEnabled = !rest.Running && !rest.PendingSave;
        startPomo.Visibility = inspectingTrace ? Visibility.Collapsed : Visibility.Visible;
        startRest.IsEnabled = !rest.Running;
        startRest.Content = rest.PendingSave ? "再保存" : "START";
        startRest.ToolTip = rest.PendingSave ? "完了した休憩の日付を保存し直す" : "選んだ分数で無音タイマーを開始";
        AutomationProperties.SetName(startRest, rest.PendingSave ? "再保存" : "START");
        resetRest.IsEnabled = !rest.PendingSave && (rest.Running || rest.Finished);
        window.Footer.Text = rest.Warning ?? (rest.Cycling ? $"POMO 20/5 · 本日{rest.TodayRounds}周 · RESETで停止" : rest.Finished ? $"休憩完了 / 成長 {growth.Days}日 · 1日1回" : settings.ClockTrace ? $"> rest.days={growth.Days:000}  stage={growth.Scene + 1:00}/04" : "NEMUMARU  /  HACKER ROOM   ·   無音タイマー");
    }
}
