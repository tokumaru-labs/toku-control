using System.Windows;

namespace TokuControl;

// Physical pixels on the console monitor. Modules keep a single right column.
public static class ModuleLayout
{
    // The arranged stack has a deliberate gutter, so its windows count as attached.
    public const double GapDip = 12;
    public static bool Attached(Rect console, Rect module, double gap)
    {
        if (console.Width <= 0 || console.Height <= 0 || module.Width <= 0 || module.Height <= 0) return false;
        double overlapX = Math.Min(console.Right, module.Right) - Math.Max(console.Left, module.Left);
        double overlapY = Math.Min(console.Bottom, module.Bottom) - Math.Max(console.Top, module.Top);
        return (overlapX > 0 && overlapY >= -gap) || (overlapY > 0 && overlapX >= -gap);
    }
    public sealed record Plan(string Mode, IReadOnlyDictionary<string, Rect> Frames);
    public static Plan? Calculate(Rect work, Point anchor, Size main, Size minimumMain,
        IReadOnlyList<(string Key, Size Size)> modules, double gap)
    {
        if (work.IsEmpty || modules.Count == 0) return null;
        double columnWidth = modules.Max(m => m.Size.Width);
        double stackHeight = modules.Sum(m => m.Size.Height) + (modules.Count - 1) * gap;
        double width = Math.Min(main.Width, work.Width - columnWidth - gap);
        double height = Math.Min(Math.Max(main.Height, stackHeight), work.Height);
        if (width < minimumMain.Width || height < minimumMain.Height || stackHeight > height) return null;
        double x = Math.Floor(Math.Clamp(anchor.X, work.Left, work.Right - width - gap - columnWidth));
        double y = Math.Floor(Math.Clamp(anchor.Y, work.Top, work.Bottom - height));
        var frames = new Dictionary<string, Rect> { ["main"] = new(x, y, width, height) };
        double top = y;
        for (int i = 0; i < modules.Count; i++)
        {
            var module = modules[i];
            // With all three visible, fill the outline down to the console bottom.
            double h = module.Size.Height + (modules.Count == 3 && i == modules.Count - 1 ? height - stackHeight : 0);
            frames[module.Key] = new(x + width + gap, top, columnWidth, h);
            top += h + gap;
        }
        return new("STACK", frames);
    }
}
