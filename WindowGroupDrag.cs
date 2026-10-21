using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TokuControl;

// Follow only the main title's DragMove lifetime, never layout/restore/resize.
// Keep physical-pixel offsets from drag start to avoid cumulative rounding drift.
public sealed class WindowGroupDrag : IDisposable
{
    readonly RetroWindow main;
    readonly Func<IEnumerable<RetroWindow>> modules;
    readonly Action<IReadOnlyList<RetroWindow>, bool> completed;
    (RetroWindow Window, Point Origin)[] followers = [];
    Point origin;
    bool inLoop, moved, captured, updating, failed;

    public WindowGroupDrag(RetroWindow main, Func<IEnumerable<RetroWindow>> modules, Action<IReadOnlyList<RetroWindow>, bool> completed)
    {
        this.main = main; this.modules = modules; this.completed = completed;
        main.TitleDragStarted += Begin;
        main.LocationChanged += LocationChanged;
        main.TitleDragFinished += End;
    }
    static bool NormalVisible(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        return hwnd != IntPtr.Zero && IsWindowVisible(hwnd) && !IsIconic(hwnd) && !IsZoomed(hwnd);
    }
    void CaptureOrigins()
    {
        if (!NormalVisible(main)) return;
        origin = WindowPlacement.NativeBounds(main).TopLeft;
        followers = modules().Where(NormalVisible).Select(w => (w, WindowPlacement.NativeBounds(w).TopLeft)).ToArray();
        captured = true;
    }
    void Follow()
    {
        if (!inLoop || updating || !NormalVisible(main)) return;
        if (!captured) CaptureOrigins(); // A drag can restore a maximized main first.
        if (!captured) return;
        var delta = WindowPlacement.NativeBounds(main).TopLeft - origin;
        moved |= delta.X != 0 || delta.Y != 0;
        var targets = followers.Where(f => NormalVisible(f.Window)).Select(f => (f.Window, f.Origin + delta)).ToArray();
        updating = true;
        try { failed |= !MoveBatch(targets); }
        finally { updating = false; }
    }
    void Begin()
    {
        inLoop = true; moved = captured = failed = false; followers = [];
        CaptureOrigins();
    }
    void LocationChanged(object? sender, EventArgs args) => Follow();
    void End()
    {
        Follow();
        bool save = inLoop && moved && captured && NormalVisible(main);
        var changed = followers.Where(f => NormalVisible(f.Window)).Select(f => f.Window).ToArray();
        inLoop = moved = captured = false; followers = [];
        if (save) completed(changed, !failed);
    }
    static bool MoveBatch((RetroWindow Window, Point Position)[] targets)
    {
        if (targets.Length == 0) return true;
        const uint flags = 0x0015; // SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE
        var batch = BeginDeferWindowPos(targets.Length);
        if (batch != IntPtr.Zero)
        {
            foreach (var (window, point) in targets)
            {
                batch = DeferWindowPos(batch, new WindowInteropHelper(window).Handle, IntPtr.Zero, (int)point.X, (int)point.Y, 0, 0, flags);
                if (batch == IntPtr.Zero) break; // Failed batch is released by Windows.
            }
            if (batch != IntPtr.Zero && EndDeferWindowPos(batch)) return true;
        }
        // A batch allocation failure should not silently leave every follower behind.
        bool result = true;
        foreach (var (window, point) in targets)
            result &= SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, (int)point.X, (int)point.Y, 0, 0, flags);
        return result;
    }
    public void Dispose()
    {
        main.TitleDragStarted -= Begin; main.LocationChanged -= LocationChanged; main.TitleDragFinished -= End;
        inLoop = moved = captured = false; followers = [];
    }
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] static extern IntPtr BeginDeferWindowPos(int count);
    [DllImport("user32.dll")] static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool EndDeferWindowPos(IntPtr batch);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
