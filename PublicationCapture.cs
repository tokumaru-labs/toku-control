// Copyright (c) 2026 Tokumaru Labs. GPL-3.0-only; see LICENSE and LICENSE-NET-EXCEPTION.
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokuControl;

public sealed partial class ConsoleWindow
{
    // Explicit developer-only capture path: Program requires preview and a separate data directory.
    // The real WPF UI is rendered with illustrative data, never a user's machine details.
    public async Task CaptureGallery(string directory)
    {
        try
        {
            if (!preview) throw new InvalidOperationException("Gallery capture requires preview mode.");
            await RefreshSystemInfoAsync();
            slowTimer.Stop(); audioTimer.Stop(); peakTimer.Stop(); crtSaveTimer.Stop();
            Directory.CreateDirectory(directory);
            Arrange(true);
            // Await any already queued layout work before replacing the displayed values.
            await Task.Delay(300);
            const ulong gib = 1073741824;
            machineSnapshot = new("Windows 11 / DEMO", "DEMO / X64", "Example 8-core CPU", 16,
                "DEMO WORKSTATION", 32 * gib, 32 * gib, 20 * gib,
                ["Example display adapter"], "C:\\", (long)(1024 * gib), (long)(640 * gib),
                TimeSpan.FromHours(6),
                [new("DISPLAY1", "Main display / DEMO", true, 2560, 1440, 0, 0, 144, 100, "0 deg"),
                 new("DISPLAY2", "Side display / DEMO", false, 1920, 1080, 2560, 0, 60, 100, "0 deg")],
                "DEMO DATA / ILLUSTRATIVE VALUES", DateTime.Now);
            RenderSystemInfo(machineSnapshot);
            var demoTime = new DateTime(2026, 10, 21, 10, 8, 20);
            topTime.Text = "2026.10.21  10:08:20\nDEMO / PREVIEW";
            clock?.SetTime(demoTime);
            if (clockDate != null) clockDate.Text = "2026.10.21  WED";
            SetAudioUi(audio.SetVolume(.62f));
            leftPeak.Level = .68; rightPeak.Level = .54;
            for (int i = 0; i < 90; i++)
            {
                UpdateMetric(cpuValue, cpuGraph, 24 + 12 * Math.Sin(i / 6d));
                UpdateMetric(ramValue, ramGraph, 38 + 2 * Math.Cos(i / 10d));
                UpdateMetric(diskValue, diskGraph, 37.5);
            }
            logs.Clear();
            Log("DEMO", "illustrative system data / simulated audio");
            Log("READY", "clock / meter / CRT settings");
            SelectSection("local");
            Footer.Text = "DEMO DATA / PREVIEW                   TOKUMARU LABS";
            await Task.Delay(250);
            CaptureGroup(Path.Combine(directory, "overview.png"));
            SelectSection("system");
            Footer.Text = "DEMO DATA / PREVIEW                   TOKUMARU LABS";
            await Task.Delay(250);
            Capture(this, Path.Combine(directory, "system.png"));
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "capture-error.txt"), ex.ToString());
            Environment.ExitCode = 1;
        }
        finally
        {
            if (Environment.ExitCode != 0) Application.Current.Shutdown(Environment.ExitCode);
            else Close();
        }
    }

    void CaptureGroup(string path)
    {
        Window[] windows = [this, .. modules.Values.Where(w => w.IsVisible)];
        foreach (var w in windows) w.UpdateLayout();
        double left = windows.Min(w => w.Left), top = windows.Min(w => w.Top);
        double width = windows.Max(w => w.Left + w.ActualWidth) - left;
        double height = windows.Max(w => w.Top + w.ActualHeight) - top;
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
            foreach (var w in windows)
                context.DrawRectangle(new VisualBrush((Visual)w.Content), null,
                    new Rect(w.Left - left, w.Top - top, w.ActualWidth, w.ActualHeight));
        var image = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        image.Render(drawing);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var file = File.Create(path); encoder.Save(file);
    }
}
