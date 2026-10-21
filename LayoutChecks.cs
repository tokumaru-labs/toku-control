using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokuControl;

public sealed partial class ConsoleWindow
{
    public async Task LayoutChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, object>();
        try
        {
            await Task.Delay(400);
            void Click(string name) => Descendants<Button>(Layout).First(b => AutomationProperties.GetName(b) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Rect[] Bounds() => new[] { WindowPlacement.NativeBounds(this) }.Concat(new[] { "clock", "meter", "crt" }.Select(k => WindowPlacement.NativeBounds(modules[k]))).ToArray();
            bool Separate() => Bounds().SelectMany((a, i) => Bounds().Skip(i + 1).Select(b => !a.IntersectsWith(b))).All(v => v);
            bool RectangleOutline(Rect[] r) => r.Skip(1).All(w => w.X == r[1].X && w.Width == r[1].Width)
                && r[1].Y == r[0].Y && r[3].Bottom == r[0].Bottom && r[1].X > r[0].Right
                && r[2].Y > r[1].Bottom && r[3].Y > r[2].Bottom;
            object[] Describe() => Bounds().Select(r => (object)new { r.X, r.Y, r.Width, r.Height }).ToArray();
            Click("SHOW ALL"); await Task.Delay(200);
            checks["showAllNoOverlap"] = Separate(); checks["showAllBounds"] = Describe();
            checks["equalWidthVerticalStackAndRectangle"] = RectangleOutline(Bounds());
            var work = WindowPlacement.WorkArea(this);
            checks["allInsideCurrentWorkArea"] = Bounds().All(work.Contains);
            checks["workArea"] = new { work.X, work.Y, work.Width, work.Height };
            var original = Bounds();
            Click("SHOW ALL"); await Task.Delay(150);
            checks["repeatLayoutStable"] = original.SequenceEqual(Bounds());
            Click("GRID"); await Task.Delay(150);
            checks["gridNoOverlap"] = Separate() && RectangleOutline(Bounds());
            Click("HIDE ALL"); Click("SHOW ALL"); await Task.Delay(150);
            checks["hiddenModulesRestored"] = modules.Values.All(w => w.IsVisible && w.WindowState == WindowState.Normal) && Separate();
            modules["meter"].Topmost = true; modules["meter"].WindowState = WindowState.Minimized;
            modules["crt"].Close(); Click("RECALL"); await Task.Delay(150);
            checks["recallRestoresMinimizedAndClosed"] = modules.Values.All(w => w.IsVisible && w.WindowState == WindowState.Normal) && Separate();
            checks["pinPreserved"] = modules["meter"].Topmost; modules["meter"].Topmost = false;
            WindowState = WindowState.Maximized; Click("SHOW ALL"); await Task.Delay(150);
            checks["maximizedMainRestoredForLayout"] = WindowState == WindowState.Normal && Separate();
            foreach (var window in modules.Values.Prepend(this))
            {
                store.Load().Windows.TryGetValue(window.Key, out var saved); var actual = WindowPlacement.NativeBounds(window);
                checks[window.Key + "PlacementSaved"] = saved != null && Math.Abs(saved.X - actual.X) < 2 && Math.Abs(saved.Y - actual.Y) < 2;
                var dpi = VisualTreeHelper.GetDpi(window);
                checks[window.Key + "SizeSaved"] = saved != null && saved.Width.HasValue && saved.Height.HasValue
                    && Math.Abs(saved.Width.Value * dpi.DpiScaleX - actual.Width) < 2 && Math.Abs(saved.Height.Value * dpi.DpiScaleY - actual.Height) < 2;
            }
            var savedFrames = Bounds(); var savedMain = store.Load().Windows["main"];
            Width = 800; Height = 600; WindowPlacement.Place(this, savedMain);
            modules["crt"].Close(); ShowModule("crt", false); await Task.Delay(150);
            checks["savedSizesRestoreRectangle"] = Bounds().SequenceEqual(savedFrames) && RectangleOutline(Bounds());
            CheckGroupDrag(checks);
            (string Key, Size Size)[] sizes = [("clock", new(420, 212)), ("meter", new(420, 182)), ("crt", new(420, 430))];
            void Scenario(string name, Rect area, double scale, string mode)
            {
                var plan = ModuleLayout.Calculate(area, new Point(area.Right - 200, area.Bottom - 100), new Size(1080 * scale, 740 * scale), new Size(720 * scale, 500 * scale), sizes.Select(m => (m.Key, new Size(m.Size.Width * scale, m.Size.Height * scale))).ToArray(), 12 * scale);
                checks[name] = plan != null && plan.Mode == mode && plan.Frames.Values.All(area.Contains)
                    && plan.Frames.Values.SelectMany((a, i) => plan.Frames.Values.Skip(i + 1).Select(b => !a.IntersectsWith(b))).All(v => v)
                    && plan.Frames["main"].Width >= 720 * scale && plan.Frames["main"].Height >= 500 * scale
                    && RectangleOutline(new[] { "main", "clock", "meter", "crt" }.Select(k => plan.Frames[k]).ToArray());
            }
            Scenario("wideScreenUsesRightStack", new(0, 0, 2560, 1400), 1, "STACK");
            Scenario("fullHdUsesRightStack", new(0, 0, 1920, 1040), 1, "STACK");
            Scenario("narrowTallScreenKeepsRectangle", new(0, 0, 1366, 900), 1, "STACK");
            Scenario("negativeMonitorOrigin", new(-1920, -200, 1920, 1040), 1, "STACK");
            Scenario("scaledPhysicalSizes", new(0, 0, 3840, 2100), 1.5, "STACK");
            checks["shortScreenDoesNotFallBackToHorizontal"] = ModuleLayout.Calculate(new(0, 0, 1366, 728), new(0, 0), new(1080, 740), new(720, 500), sizes, 12) == null;
            checks["impossibleLayoutReported"] = ModuleLayout.Calculate(new(0, 0, 800, 600), new(0, 0), new(1080, 740), new(720, 500), sizes, 12) == null;
            slowTimer.Stop(); clock!.SetTime(new DateTime(2026, 10, 21, 10, 6, 20));
            await Task.Delay(100);
            Capture(modules["clock"], Path.Combine(directory, "clock-reference.png"));
            CaptureArrangement(Path.Combine(directory, "arrangement.png"));
            checks["passed"] = checks.Values.OfType<bool>().All(v => v);
            File.WriteAllText(Path.Combine(directory, "layout-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(checks["passed"] is true ? 0 : 1);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString()); Application.Current.Shutdown(1); }
    }

    // Render the actual WPF window visuals at their measured native positions.
    // This is a layout proof, not a desktop screenshot containing other apps.
    void CaptureArrangement(string path)
    {
        var windows = new[] { this }.Cast<RetroWindow>().Concat(new[] { "clock", "meter", "crt" }.Select(k => modules[k])).ToArray();
        var frames = windows.Select(WindowPlacement.NativeBounds).ToArray();
        double left = frames.Min(r => r.Left), top = frames.Min(r => r.Top);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Retro.Background, null, new Rect(0, 0, frames.Max(r => r.Right) - left, frames.Max(r => r.Bottom) - top));
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].UpdateLayout(); var frame = frames[i]; frame.Offset(-left, -top);
                dc.DrawRectangle(new VisualBrush((Visual)windows[i].Content) { Stretch = Stretch.Fill }, null, frame);
            }
        }
        var bitmap = new RenderTargetBitmap((int)(frames.Max(r => r.Right) - left), (int)(frames.Max(r => r.Bottom) - top), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path); encoder.Save(output);
    }
}
