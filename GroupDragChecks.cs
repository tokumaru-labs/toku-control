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
        // A separately moved small window establishes its own new group offset.
        var beforeIndependent = Bounds(); Move(modules["clock"], beforeIndependent[1].TopLeft + new Vector(17, 9));
        checks["smallWindowMoveIndependent"] = Bounds().Where((_, i) => i != 1).SequenceEqual(beforeIndependent.Where((_, i) => i != 1));
        var customized = Bounds(); Begin(); Move(this, customized[0].TopLeft + new Vector(11, -8));
        checks["groupKeepsCustomizedOffsets"] = Shifted(customized, new(11, -8)); EndTitleDrag();
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
        for (int i = 0; i < all.Length; i++) Move(all[i], original[i].TopLeft);
        Arrange(true);
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] static extern bool DragCheckPosition(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
