using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace TokuControl;

internal sealed class RestCheckTime : TimeProvider
{
    TimeSpan elapsed;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => elapsed.Ticks;
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 1, 2, 23, 50, 0, TimeSpan.Zero) + elapsed;
    public void Add(TimeSpan delta) => elapsed += delta;
}

public sealed partial class ConsoleWindow
{
    internal async Task CheckRestClock(string directory, RestCheckTime time)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, bool>(); string? error = null;
        try
        {
            await Task.Delay(150); var w = modules["clock"]; w.UpdateLayout();
            void Click(Button b) => b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Wheel(int delta) => minuteWheel.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 1, delta) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
            var journal = new HackerJournal(Path.Combine(store.DirectoryPath, "nemumaru-preview"));
            checks["new room and prepared local art"] = rest.Growth.Days == 0 && room.Source == HackerScenes.Value[0] && HackerScenes.Value.Distinct().Count() == 4;
            checks["compact clock, art and controls visible"] = w.ActualWidth == 420 && w.ActualHeight == 212 && room.ActualHeight > 90 &&
                new FrameworkElement[] { room, roomLabel, clockTheme, clock!, clockDate!, minuteWheel, startRest, resetRest, timerBar }.All(el =>
                {
                    var rect = el.TransformToAncestor(w).TransformBounds(new Rect(new Size(el.ActualWidth, el.ActualHeight)));
                    return rect.Width > 0 && rect.Height > 0 && rect.Left >= 0 && rect.Right <= w.ActualWidth && rect.Top >= 0 && rect.Bottom <= w.ActualHeight;
                });
            minuteWheel.Value = 5; Wheel(-60); Wheel(-60); bool ten = minuteWheel.Value == 10;
            Wheel(-120);
            checks["high resolution wheel selects 5,10,15 without starting"] = ten && minuteWheel.Value == 15 && !rest.Running;
            minuteWheel.Value = 120; Wheel(-120); bool bounded = minuteWheel.Value == 120;
            minuteWheel.Value = 5; Wheel(120); checks["wheel stays in range"] = bounded && minuteWheel.Value == 5;
            void KeyPress(Key key) => minuteWheel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(minuteWheel), 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
            KeyPress(System.Windows.Input.Key.Down); bool keyTen = minuteWheel.Value == 10; KeyPress(System.Windows.Input.Key.Home);
            checks["keyboard selects without starting"] = keyTen && minuteWheel.Value == 5 && !rest.Running;
            Click(startRest); time.Add(TimeSpan.FromMinutes(2)); Tick();
            checks["remaining time, visual bar and running controls"] = remaining.Text.Contains("03:00") && Math.Abs(timerBar.Value - .4) < .001 && !minuteWheel.IsEnabled && !startRest.IsEnabled;
            clockTheme.SelectedIndex = 1;
            checks["TRACE selection preserves the running timer and growth"] = rest.Running && remaining.Text.Contains("03:00") &&
                minuteWheel.Value == 5 && journal.Read().Days == 0 && room.Source == TraceScenes.Value[0] && store.Load().ClockTrace;
            w.UpdateLayout();
            checks["TRACE console fits the clock"] = traceConsole.IsVisible && traceConsole.ActualWidth > 170 &&
                traceConsole.ActualHeight >= 25 && traceConsole.TransformToAncestor(w).Transform(new Point()).Y >= 0;
            var first = traceConsole.Prompt;
            await Task.Delay(400);
            checks["command types horizontally"] = !SystemParameters.ClientAreaAnimation ||
                traceConsole.Prompt.StartsWith("toku> rest") && traceConsole.Prompt != first;
            Click(traceMotion);
            checks["motion off reveals the full command and actual timer"] = !traceConsole.Animating &&
                traceConsole.Prompt == "toku> rest.start 5m" && traceConsole.Reply.Contains("LEFT 03:00") &&
                !store.Load().ClockCodeMotion && remaining.Text.Contains("03:00");
            Click(traceMotion);
            w.Hide();
            checks["hidden clock stops typing"] = !traceConsole.Animating;
            w.Show(); w.UpdateLayout();
            clockTheme.SelectedIndex = 0;
            checks["room has no console or motion control"] = !traceConsole.IsVisible && !traceConsole.Animating && !traceMotion.IsVisible;
            checks["room can be restored while running"] = rest.Running && room.Source == HackerScenes.Value[0] && !store.Load().ClockTrace;
            Capture(w, Path.Combine(directory, "clock-running.png"));
            Click(resetRest); checks["reset gives no growth"] = !rest.Running && rest.Growth.Days == 0 && journal.Read().Days == 0;
            Click(startRest); w.Hide(); time.Add(TimeSpan.FromMinutes(5)); Tick(); Tick();
            checks["hidden clock credits one completion"] = rest.Finished && journal.Read().Days == 1;
            ShowModule("clock", false); Tick(); w.UpdateLayout();
            checks["room changes after first finished break"] = room.Source == HackerScenes.Value[1] && roomLabel.Text.Contains("001");
            Capture(w, Path.Combine(directory, "clock-room.png"));
            clockTheme.SelectedIndex = 1; w.UpdateLayout();
            checks["TRACE shares the earned day"] = rest.Growth.Days == 1 && journal.Read().Days == 1 &&
                room.Source == TraceScenes.Value[1] && TraceScenes.Value.Distinct().Count() == 4 && roomLabel.Text.Contains("信号同期 · DAY 001");
            await Task.Delay(1900);
            checks["finished timer returns the earned node and then rests"] = traceConsole.Prompt == "toku> rest.result" &&
                traceConsole.Reply.Contains("COMPLETE  /  NODE 02") && !traceConsole.Animating;
            Capture(w, Path.Combine(directory, "clock-trace.png"));
            Click(startRest); Click(resetRest);
            await Task.Delay(1900);
            checks["a rapid start and cancel replaces the unfinished command"] = traceConsole.Prompt == "toku> rest.cancel" &&
                traceConsole.Reply.Contains("CANCELLED") && !traceConsole.Animating && journal.Read().Days == 1;
            // Cross midnight during this second timer. Closing only the clock must not cancel it.
            Click(startRest); w.Close(); time.Add(TimeSpan.FromMinutes(5)); Tick(); ShowModule("clock", false);
            checks["reopened clock retains TRACE"] = clockTheme.SelectedIndex == 1 && room.Source == TraceScenes.Value[1] && store.Load().ClockTrace;
            checks["closed module continues; date is the scheduled finish day"] = rest.Growth.Days == 2 && journal.Read().LastDay == new DateOnly(2026, 1, 3) && !rest.Running;
            Click(startRest); time.Add(TimeSpan.FromMinutes(5)); Tick();
            checks["two sessions on same local day count once"] = rest.Growth.Days == 2;
            time.Add(TimeSpan.FromDays(2)); Tick();
            checks["idle days do not increase or erase growth"] = rest.Growth.Days == 2 && journal.Read().Days == 2;
            var future = new DateOnly(2026, 1, 8);
            await Task.WhenAll(Task.Run(() => journal.Complete(future)), Task.Run(() => new HackerJournal(Path.Combine(store.DirectoryPath, "nemumaru-preview")).Complete(future)));
            checks["two writers share one daily credit and reload it"] = journal.Read().Days == 3;
            checks["art milestones"] = new[] { 0, 1, 6, 7, 29, 30, 365 }.Select(d => new HackerGrowth(d).Scene).SequenceEqual(new[] { 0, 1, 1, 2, 2, 3, 3 });
            string broken = Path.Combine(store.DirectoryPath, "blocked-journal"); File.WriteAllText(broken, "fixture");
            var failed = new HackerRest(new HackerJournal(broken), time); failed.Start(5); time.Add(TimeSpan.FromMinutes(5)); failed.Advance();
            checks["failed save remains pending, no false growth"] = failed.PendingSave && failed.Growth.Days == 0 && failed.Warning != null && !failed.Start(5);
            checks["persistent failure prevents silent exit"] = !failed.PrepareToExit() && failed.PendingSave;
            var originalDay = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
            File.Delete(broken);
            var otherWindow = new HackerJournal(broken);
            otherWindow.Complete(originalDay.AddDays(1));
            checks["exit retries original date even after a newer completion"] = failed.PrepareToExit() && !failed.PendingSave && failed.Growth.Days == 2 &&
                failed.Growth.EarnedDates!.Contains(originalDay) && otherWindow.Read().LastDay == originalDay.AddDays(1);
            failed.RetrySave();
            checks["late retry cannot double credit"] = otherWindow.Complete(originalDay).Days == 2;
            string capDir = Path.Combine(store.DirectoryPath, "cap-fixture"); Directory.CreateDirectory(capDir);
            string capFile = Path.Combine(capDir, "growth-v1.json");
            File.WriteAllText(capFile, JsonSerializer.Serialize(new HackerGrowth(364, future)));
            var cap = new HackerJournal(capDir);
            checks["growth stops at 365 days"] = cap.Complete(future.AddDays(1)).Days == 365 && cap.Complete(future.AddDays(2)).Days == 365;
            File.WriteAllText(capFile, "{}");
            try { cap.Complete(future); checks["unreadable progress preserved"] = false; }
            catch (InvalidDataException) { checks["unreadable progress preserved"] = File.ReadAllText(capFile) == "{}"; }
        }
        catch (Exception ex) { error = ex.ToString(); }
        bool passed = error == null && checks.Count > 10 && checks.Values.All(v => v);
        File.WriteAllText(Path.Combine(directory, "clock-checks.json"), JsonSerializer.Serialize(new { passed, checks, error }, new JsonSerializerOptions { WriteIndented = true }));
        Application.Current.Shutdown(passed ? 0 : 1);
    }
}
