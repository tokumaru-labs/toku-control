using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace TokuControl;

internal sealed class TraceConsole : Border
{
    readonly Window owner;
    readonly TextBlock prompt = new() { Foreground = Retro.Green };
    readonly TextBlock result = new() { Foreground = Retro.Cyan };
    readonly Stopwatch elapsed = new();
    readonly DispatcherTimer timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(65) };
    string command = "", reply = "";
    bool motion, settled;

    public TraceConsole(Window owner)
    {
        this.owner = owner;
        Background = Retro.Panel; BorderBrush = Retro.Border; BorderThickness = new Thickness(1);
        Padding = new Thickness(5, 1, 2, 1); Margin = new Thickness(3, 0, 3, 1);
        var lines = new StackPanel(); lines.Children.Add(prompt); lines.Children.Add(result); Child = lines;
        foreach (var line in new[] { prompt, result })
        {
            line.FontFamily = new FontFamily("Consolas"); line.FontSize = 11;
        }
        timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) => Refresh();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) => Refresh();
        owner.StateChanged += (_, _) => Refresh();
    }

    internal string Prompt => prompt.Text;
    internal string Reply => result.Text;
    internal bool Animating => timer.IsEnabled;

    public void Display(bool show, bool animate, string next, string output)
    {
        if (command != next)
        {
            Stop(); elapsed.Reset(); settled = !show || !animate;
        }
        command = next; reply = output; motion = animate;
        Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Refresh();
    }

    void Refresh()
    {
        if (!IsLoaded || !IsVisible || owner.WindowState == WindowState.Minimized) { settled = true; Stop(); return; }
        if (!motion || !SystemParameters.ClientAreaAnimation) settled = true;
        double age = elapsed.Elapsed.TotalSeconds;
        int count = settled ? command.Length : Math.Min(command.Length, (int)(age * 17));
        prompt.Text = "toku> " + command[..count] + (count < command.Length ? "▌" : "");
        result.Text = count == command.Length ? reply : " ";
        if (count == command.Length) { settled = true; Stop(); }
        else if (!timer.IsEnabled) { elapsed.Start(); timer.Start(); }
    }

    void Stop() { timer.Stop(); elapsed.Stop(); }
}
