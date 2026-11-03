using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TokuControl;

internal sealed class NightMandala : FrameworkElement
{
    // Presentation bands on the painting, not historical or religious divisions.
    static readonly int[] BandDays = [8, 12, 20, 32, 48, 64, 80, 101];
    static readonly double[] Radii = [.14, .26, .40, .55, .69, .81, .91, .98, 1.42];
    static readonly BitmapSource Painting = LoadPainting();
    static readonly Point Center = new(128, 128);
    static readonly Rect Frame = new(4, 4, 248, 248);
    int days = -1;

    static BitmapSource LoadPainting()
    {
        var image = new BitmapImage(new Uri("pack://application:,,,/TokuControl;component/Assets/cma-1993-4.jpg"));
        image.Freeze();
        return image;
    }

    public NightMandala() => ToolTip = "宵環曼荼羅 · 休憩した日ごとに彩色が広がります。\n原画：Twenty-three Deity Nairatma Mandala（1375年頃）\nCleveland Museum of Art 1993.4 · CC0";

    public void SetDays(int value)
    {
        value = Math.Clamp(value, 0, 365);
        if (value == days) return;
        days = value;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth, ActualHeight) / 256;
        if (scale <= 0) return;
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, (ActualWidth - 256*scale)/2, (ActualHeight - 256*scale)/2));
        dc.DrawRectangle(Retro.Brush("#100D18"), null, new Rect(0,0,256,256));
        dc.PushClip(new RectangleGeometry(Frame));
        double height = Frame.Width * Painting.PixelHeight / Painting.PixelWidth;
        var crop = new Rect(Frame.X, Center.Y - .494 * height, Frame.Width, height);
        if (days >= 365) dc.DrawImage(Painting, crop);
        else
        {
            dc.PushOpacity(.32); dc.DrawImage(Painting, crop); dc.Pop();
            int left = Math.Max(0, days), band = 0;
            while (left >= BandDays[band])
            {
                left -= BandDays[band];
                band++;
            }
            double radius = Frame.Width / 2 * Radii[band];
            dc.PushClip(new EllipseGeometry(Center, radius, radius)); dc.DrawImage(Painting, crop); dc.Pop();
            double outer = Frame.Width / 2 * Radii[band+1];
            for (int gate=0; gate<4; gate++)
            {
                int earned = (left + 3 - gate) / 4;
                if (earned == 0) continue;
                int slots = (BandDays[band] + 3 - gate) / 4;
                double axis = (gate-1)*Math.PI/2, half = Math.PI/4*earned/slots;
                Point Edge(double angle) => new(Center.X + outer*Math.Cos(angle), Center.Y + outer*Math.Sin(angle));
                var sector = new StreamGeometry();
                using (var path = sector.Open())
                {
                    path.BeginFigure(Center, true, true); path.LineTo(Edge(axis-half), true, false);
                    path.ArcTo(Edge(axis+half), new Size(outer,outer), 0, false, SweepDirection.Clockwise, true, false);
                }
                dc.PushClip(sector); dc.DrawImage(Painting,crop); dc.Pop();
            }
        }
        dc.Pop();
        dc.DrawRectangle(null, new Pen(Retro.Brush("#B59A65"), 1), Frame);
        dc.Pop();
    }
}
