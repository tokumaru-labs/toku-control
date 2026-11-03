using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace TokuControl;

internal sealed record TraceRun(int Number, int Target, int Elapsed = 0, string State = "RUN");

internal sealed class TraceHistory
{
    readonly List<TraceRun> runs = [];
    public IReadOnlyList<TraceRun> Runs => runs;
    public int Revision { get; private set; }
    int serial;

    public void Start(int target)
    {
        if (runs.Count == 6) runs.RemoveAt(0);
        runs.Add(new(++serial, target));
        Revision++;
    }

    public void Finish(int elapsed, bool complete)
    {
        if (runs.Count == 0 || runs[^1].State != "RUN") return;
        var entry = runs[^1];
        runs[^1] = entry with { Elapsed = Math.Clamp(elapsed, 0, entry.Target), State = complete ? "DONE" : "CUT" };
        Revision++;
    }

    public static string Clock(int seconds) => $"{seconds / 60:00}:{seconds % 60:00}";
}

internal sealed class TraceDetails : Grid
{
    readonly StackPanel chart = new(), events = new();
    int version = -1, previousElapsed = -1;
    readonly TextBlock summary = Line("", Retro.Cyan, 11);
    public TraceDetails()
    {
        Background = Retro.Panel;
        Margin = new Thickness(5, 2, 5, 2);
        ColumnDefinitions.Add(new() { Width = new GridLength(0.47, GridUnitType.Star) });
        ColumnDefinitions.Add(new() { Width = new GridLength(0.53, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        SetColumnSpan(summary, 2); SetRow(summary, 1); Children.Add(summary);
        Pane("RUN PROFILE", "直近6回  ·  実測 / 設定", chart, 0);
        Pane("EVENT TAPE", "起動中の記録  ·  新しい順", events, 1);
    }

    void Pane(string title, string hint, StackPanel content, int col)
    {
        var box = new DockPanel { Margin = new Thickness(3, 1, 3, 1) };
        var header = new StackPanel();
        header.Children.Add(Line(title, Retro.Cyan, 11)); header.Children.Add(Line(hint, Retro.Dim, 10));
        DockPanel.SetDock(header, Dock.Top); box.Children.Add(header);
        box.Children.Add(new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        SetColumn(box, col); Children.Add(box);
    }

    static TextBlock Line(string text, Brush ink, double size) => new() {
        Text = text, Foreground = ink, FontSize = size, Margin = new Thickness(2, 1, 0, 0),
        FontFamily = new FontFamily("Consolas, Yu Gothic UI")
    };

    internal void Refresh(HackerRest rest)
    {
        var history = rest.History;
        int live = rest.Running && !rest.Focusing ? rest.ElapsedSeconds : -1;
        if (version == history.Revision && live == previousElapsed) return;
        if (version != history.Revision)
        {
            events.Children.Clear();
            foreach (var row in history.Runs.Reverse())
            {
                if (row.State != "RUN") events.Children.Add(Line(
                    $"#{row.Number:00} {(row.State == "DONE" ? "rest.result" : "rest.cancel")} +{TraceHistory.Clock(row.Elapsed)}",
                    row.State == "DONE" ? Retro.Cyan : Retro.Amber, 10));
                events.Children.Add(Line($"#{row.Number:00} rest.start  {row.Target / 60}m", Retro.Green, 11));
            }
            if (history.Runs.Count == 0) events.Children.Add(Line("> awaiting start", Retro.Dim, 11));
        }
        version = history.Revision; previousElapsed = live;
        chart.Children.Clear();
        foreach (var row in history.Runs.Reverse())
        {
            int elapsed = row.State == "RUN" ? Math.Max(0, live) : row.Elapsed;
            string label = $"#{row.Number:00} {TraceHistory.Clock(elapsed)} / {TraceHistory.Clock(row.Target)}";
            chart.Children.Add(Line(label + $"  {elapsed * 100 / row.Target}%", Retro.Text, 11));
            var bar = new ProgressBar { Height = 6, Minimum = 0, Maximum = row.Target, Value = elapsed,
                Margin = new Thickness(2, 1, 4, 2), Background = Retro.Background,
                Foreground = row.State == "CUT" ? Retro.Amber : row.State == "RUN" ? Retro.Green : Retro.Cyan };
            AutomationProperties.SetName(bar, label + " " + row.State); chart.Children.Add(bar);
        }
        int used = history.Runs.Sum(r => r.State == "RUN" ? Math.Max(0, live) : r.Elapsed);
        summary.Text = $"Σ USED {TraceHistory.Clock(used)}   ·   DONE {history.Runs.Count(r => r.State == "DONE")}   CUT {history.Runs.Count(r => r.State == "CUT")}";
        if (history.Runs.Count == 0) chart.Children.Add(Line("休憩を始めると表示", Retro.Dim, 11));
    }
}
