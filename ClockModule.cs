using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokuControl;

public sealed partial class ConsoleWindow
{
    static readonly Lazy<BitmapSource[]> HackerScenes = new(() =>
    {
        var atlas = new BitmapImage(new Uri("pack://application:,,,/TokuControl;component/Assets/NemumaruHacker.png"));
        int width = atlas.PixelWidth / 2, height = atlas.PixelHeight / 2;
        return Enumerable.Range(0, 4).Select(i =>
        {
            // Leave the one-pixel dividing line of the generated atlas outside each viewport.
            var frame = new CroppedBitmap(atlas, new Int32Rect(i % 2 * width + 2, i / 2 * height + 2, width - 4, height - 4));
            frame.Freeze(); return (BitmapSource)frame;
        }).ToArray();
    });
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
        DockPanel.SetDock(roomLabel, Dock.Bottom); art.Children.Add(roomLabel); art.Children.Add(room); body.Children.Add(art);

        var controls = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(controls, 1); body.Children.Add(controls);
        clock = new SegmentClock { Height = 44, Margin = new Thickness(2, 0, 2, 0) }; controls.Children.Add(clock);
        clockDate = Retro.Label("", Retro.Text, 10); clockDate.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(clockDate);
        minuteWheel = new MinuteWheel { Value = selectedMinutes };
        minuteWheel.ValueChanged += (_, _) => selectedMinutes = (int)minuteWheel.Value;
        startRest = Retro.Button("START", "選んだ分数で無音タイマーを開始", () =>
        {
            if (rest.PendingSave) rest.RetrySave();
            else if (rest.Start(selectedMinutes)) Log("INFO", $"silent timer set / {selectedMinutes} min");
            RenderRest(window);
        });
        resetRest = Retro.Button("RESET", "タイマーを中断して待機に戻る", () => { rest.Reset(); RenderRest(window); });
        var pin = Retro.Button("PIN", "時計の最前面固定を切り替える", () => { window.Topmost = !window.Topmost; SavePositions(); });
        var actions = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        remaining = Retro.Label("", Retro.Green, 12); actions.Children.Add(remaining);
        actions.Children.Add(Retro.Row(startRest, resetRest, pin));
        var row = Retro.Row(minuteWheel, actions); row.HorizontalAlignment = HorizontalAlignment.Center; controls.Children.Add(row);
        timerBar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 5, Foreground = Retro.Green, Background = Retro.Panel, Margin = new Thickness(4, 2, 4, 1) };
        AutomationProperties.SetName(timerBar, "休憩の進捗"); controls.Children.Add(timerBar);
        window.Layout.Children.Add(body);
        RenderRest(window);
        return window;
    }
    void RenderRest(RetroWindow window)
    {
        var growth = rest.Growth;
        room.Source = HackerScenes.Value[growth.Scene];
        roomLabel.Text = $"{growth.SceneName} · DAY {growth.Days:000}";
        AutomationProperties.SetName(room, "ねむまる・ハッカー版 / " + roomLabel.Text);
        int seconds = (int)Math.Ceiling(rest.Remaining.TotalSeconds);
        remaining.Text = rest.Running ? $"残り {seconds / 60:00}:{seconds % 60:00}" : rest.Finished ? "休憩完了" : "休憩タイマー";
        timerBar.Value = rest.Running ? 1 - rest.Remaining.TotalMinutes / rest.Minutes : rest.Finished ? 1 : 0;
        minuteWheel.IsEnabled = !rest.Running && !rest.PendingSave;
        startRest.IsEnabled = !rest.Running;
        startRest.Content = rest.PendingSave ? "再保存" : "START";
        startRest.ToolTip = rest.PendingSave ? "完了した休憩の日付を保存し直す" : "選んだ分数で無音タイマーを開始";
        AutomationProperties.SetName(startRest, rest.PendingSave ? "再保存" : "START");
        resetRest.IsEnabled = !rest.PendingSave && (rest.Running || rest.Finished);
        window.Footer.Text = rest.Warning ?? (rest.Finished ? $"休憩完了 / 成長 {growth.Days}日 · 1日1回" : "NEMUMARU  /  HACKER ROOM   ·   無音タイマー");
    }
}
