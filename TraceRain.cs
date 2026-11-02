using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace TokuControl;

// The stream is painted over the side rails of the TRACE artwork only.
internal sealed class TraceRain : FrameworkElement
{
    const string Alphabet = "01:A7<>F/E[]9+=*ネムト";
    readonly Stopwatch elapsed = new();
    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(125) };
    readonly Window owner;
    readonly Typeface face = new("Consolas");
    readonly Brush ink = Retro.Brush("#7DFFC3");
    FormattedText[] glyphs = [];
    double glyphDpi;
    bool motion;

    public TraceRain(Window owner)
    {
        this.owner = owner;
        IsHitTestVisible = false;
        timer.Tick += (_, _) => InvalidateVisual();
        IsVisibleChanged += (_, _) => Sync();
        Loaded += (_, _) => Sync();
        Unloaded += (_, _) => Stop();
        owner.StateChanged += (_, _) => Sync();
    }

    internal bool Flowing => timer.IsEnabled;

    public void Configure(bool show, bool animate)
    {
        motion = animate;
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Sync();
    }

    void Sync()
    {
        if (motion && IsLoaded && IsVisible && owner.WindowState != WindowState.Minimized && SystemParameters.ClientAreaAnimation)
        {
            if (!timer.IsEnabled) { elapsed.Start(); timer.Start(); }
        }
        else Stop();
    }

    void Stop() { timer.Stop(); elapsed.Stop(); }

    protected override void OnRender(DrawingContext dc)
    {
        double size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        if (glyphDpi != dpi)
        {
            glyphDpi = dpi;
            glyphs = Alphabet.Select(c => new FormattedText(c.ToString(), CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, face, 16, ink, dpi)).ToArray();
        }
        dc.PushTransform(new MatrixTransform(size / 320, 0, 0, size / 320, (ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushClip(new RectangleGeometry(new Rect(6, 32, 308, 192)));
        double seconds = elapsed.Elapsed.TotalSeconds;
        for (int lane = 0; lane < 8; lane++)
        {
            double x = lane < 4 ? 9 + lane * 19 : 243 + (lane - 4) * 19;
            double distance = seconds * (22 + lane % 4 * 5) + lane * 53;
            double head = distance % 279;
            int sequence = (int)(distance / 13) % Alphabet.Length;
            for (int tail = 0; tail < 6; tail++)
            {
                double y = head - tail * 13;
                if (y < 18 || y > 224) continue;
                int glyph = (sequence + lane * 7 + tail * 11) % Alphabet.Length;
                dc.PushOpacity(tail == 0 ? .94 : .64 - tail * .085);
                dc.DrawText(glyphs[glyph], new Point(x, y));
                dc.Pop();
            }
        }
        dc.Pop();
        dc.Pop();
    }
}
