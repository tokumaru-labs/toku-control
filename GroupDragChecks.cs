using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace TokuControl;

public sealed partial class ConsoleWindow
{
    // Title-drag lifecycle simulation with real HWND positions. A real mouse
    // drag is checked separately through Computer Use before deployment.
    void CheckGroupDrag(Dictionary<string, object> checks)
    {
        var all = new[] { this }.Concat(new[] { "clock", "meter", "crt" }.Select(k => modules[k])).ToArray();
        Rect[] Bounds() => all.Select(WindowPlacement.NativeBounds).ToArray();
        void Move(Window w, Point p) => DragCheckPosition(new WindowInteropHelper(w).Handle, IntPtr.Zero, (int)p.X, (int)p.Y, 0, 0, 0x0015);
        bool Shifted(Rect[] before, Vector delta) => Bounds().Zip(before).All(p => p.First.TopLeft == p.Second.TopLeft + delta && p.First.Size == p.Second.Size);
        void Begin() => BeginTitleDrag();
        var initial = Bounds();
        // Exercise the real drag hook independently of whether the desktop is large enough for SHOW ALL.
        double gap = WindowPlacement.ModuleGap(this);
        for (int i = 1; i < all.Length; i++)
            Move(all[i], new Point(initial[0].Right + gap, initial[0].Top + (i - 1) * 40));
        var original = Bounds();
        Move(this, original[0].TopLeft + new Vector(30, 12));
        checks["groupIgnoresProgrammaticMove"] = Bounds().Skip(1).SequenceEqual(original.Skip(1));
        Move(this, original[0].TopLeft);
        modules["meter"].Topmost = true;
        Begin(); Move(this, original[0].TopLeft + new Vector(43, -19));
        checks["groupFollowsBeforeMouseRelease"] = Shifted(original, new(43, -19));
        Move(this, original[0].TopLeft + new Vector(-27, -6));
        checks["groupBackAndForthWithoutDrift"] = Shifted(original, new(-27, -6));
        EndTitleDrag();
        checks["groupKeepsPin"] = modules["meter"].Topmost;
        checks["groupPositionsSavedAtRelease"] = all.All(w =>
        {
            var r = WindowPlacement.NativeBounds(w); return store.Load().Windows.TryGetValue(w.Key, out var saved) && saved.X == r.X && saved.Y == r.Y;
        });
        modules["meter"].Topmost = false;
        // Pull the clock away; keep the meter and CRT in the arranged column.
        var beforeIndependent = Bounds(); Move(modules["clock"], beforeIndependent[1].TopLeft + new Vector(64, 9));
        checks["smallWindowMoveIndependent"] = Bounds().Where((_, i) => i != 1).SequenceEqual(beforeIndependent.Where((_, i) => i != 1));
        var customized = Bounds(); Begin();
        foreach (int x in new[] { 64, 74 }) // Cross into range during the drag; do not pick up the clock mid-gesture.
        {
            var shift = new Vector(x, -8);
            Move(this, customized[0].TopLeft + shift);
            var actual = Bounds();
            checks["dragKeepsDetachedClockStationary" + x] = actual[1] == customized[1] &&
                new[] { 0, 2, 3 }.All(i => actual[i].TopLeft == customized[i].TopLeft + shift);
        }
        EndTitleDrag();
        var main = WindowPlacement.NativeBounds(this);
        Move(modules["clock"], new Point(main.Right + WindowPlacement.ModuleGap(this), main.Top + 8));
        var reattached = Bounds(); Begin(); Move(this, reattached[0].TopLeft + new Vector(11, -8));
        checks["reattachedClockFollowsNextDrag"] = Shifted(reattached, new(11, -8)); EndTitleDrag();
        var beforeResize = Bounds(); Width += 12;
        checks["groupIgnoresResize"] = Bounds().Skip(1).SequenceEqual(beforeResize.Skip(1)); Width -= 12;
        modules["clock"].Hide(); modules["meter"].WindowState = WindowState.Minimized;
        var excluded = Bounds(); var hiddenSaved = store.Load().Windows["clock"];
        Begin(); Move(this, excluded[0].TopLeft + new Vector(19, -8));
        checks["groupSkipsHiddenAndMinimized"] = Bounds()[1] == excluded[1] && Bounds()[2] == excluded[2]
            && Bounds()[3].TopLeft == excluded[3].TopLeft + new Vector(19, -8);
        EndTitleDrag();
        checks["groupDoesNotOverwriteHiddenPosition"] = store.Load().Windows["clock"] == hiddenSaved;
        var beforeMinimize = WindowPlacement.NativeBounds(modules["crt"]);
        WindowState = WindowState.Minimized; WindowState = WindowState.Normal;
        checks["mainMinimizeDoesNotPullModules"] = WindowPlacement.NativeBounds(modules["crt"]) == beforeMinimize;
        modules["clock"].Show(); modules["meter"].WindowState = WindowState.Normal;
        for (int i = 0; i < all.Length; i++) Move(all[i], initial[i].TopLeft);
        SavePositions();
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] static extern bool DragCheckPosition(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
