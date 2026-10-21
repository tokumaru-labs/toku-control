using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TokuControl;

public sealed partial class ConsoleWindow
{
    // Exercises the real CRT controls and renders a stationary window through the
    // same ApplyAll path. No moving clock, graph, slider or value label can make
    // the pixel-difference checks pass merely by changing its own content.
    public async Task CrtChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, object>();
        try
        {
            if (!preview) throw new InvalidOperationException("CRT checks require --preview and a separate --data-dir.");
            await Task.Delay(350);
            ShowModule("crt");
            var probe = new RetroWindow("crt-probe", "CRT EFFECT CHECK", 390, 210);
            probe.Layout.Children.Add(Retro.Col(
                Retro.Label("TOKU CONTROL", Retro.Green, 22),
                Retro.Label("RGB  /  0123456789", Retro.Cyan, 18),
                Retro.Label("PHOSPHOR / LOCAL DISPLAY", Retro.Text, 12),
                new Border { Height = 18, Margin = new Thickness(6), Background = Retro.Title }));
            probe.Footer.Text = "STATIC REFERENCE / NO CHANGING VALUES";
            modules.Add("crt-probe", probe);
            probe.Show();

            string[] names = ["SCANLINE", "BLOOM", "RGB EDGE", "NOISE", "VIGNETTE"];
            Slider SliderFor(string name) => Descendants<Slider>(modules["crt"]).Single(s => AutomationProperties.GetName(s) == name);
            void ClickCrt(string name) => Descendants<Button>(modules["crt"]).Single(b => AutomationProperties.GetName(b) == name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            void Zero()
            {
                settings.Crt = true;
                foreach (string name in names) { var s = SliderFor(name); s.Value = s.Minimum; }
                ApplyAll();
            }
            async Task<BitmapSource> Render(string name)
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(80);
                probe.UpdateLayout();
                var visual = (FrameworkElement)probe.Content;
                var bmp = new RenderTargetBitmap((int)visual.ActualWidth, (int)visual.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bmp.Render(visual); bmp.Freeze();
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bmp));
                using var output = File.Create(Path.Combine(directory, name + ".png")); encoder.Save(output);
                return bmp;
            }
            Zero();
            var zero = await Render("all-zero");
            foreach (string name in names)
            {
                Zero(); var slider = SliderFor(name); slider.Value = slider.Maximum;
                var maximum = await Render(name.ToLowerInvariant().Replace(' ', '-') + "-max");
                var delta = PixelDifference(zero, maximum);
                checks[name + "PixelDelta"] = delta;
                checks[name + "VisibleChange"] = delta.ChangedFraction >= .008 && delta.MeanChannelDelta >= .30;
                checks[name + "KeyboardFineStep"] = slider.SmallChange > 0 && slider.SmallChange <= (slider.Maximum - slider.Minimum) * .02;
            }
            foreach (string name in names) SliderFor(name).Value = SliderFor(name).Maximum;
            ClickCrt("RAW");
            var raw = await Render("raw");
            checks["rawDisablesAllEffects"] = PixelDifference(zero, raw).ChangedFraction == 0;
            checks["rawDisablesEffectSliders"] = names.All(name => !SliderFor(name).IsEnabled);
            ClickCrt("CRT ON");
            checks["crtOnReenablesSliders"] = names.All(name => SliderFor(name).IsEnabled);
            ClickCrt("RESTORE");
            checks["restoreRefreshesSliders"] = Math.Abs(SliderFor("BLOOM").Value / SliderFor("BLOOM").Maximum - .44) < .001;
            var dim = Descendants<Button>(this).Single(b => AutomationProperties.GetName(b) == "DIM");
            dim.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            checks["dimRefreshesBloomSlider"] = Math.Abs(SliderFor("BLOOM").Value / SliderFor("BLOOM").Maximum - .12) < .001;
            var bloom = SliderFor("BLOOM"); bloom.Value = bloom.Maximum * .70;
            bloom.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            checks["dragPersistsSettings"] = Math.Abs(store.Load().Bloom - .35) < .001;
            ClickCrt("RESTORE");
            await Render("default");
            Capture(modules["crt"], Path.Combine(directory, "settings-window.png"));
            checks["passed"] = checks.Values.OfType<bool>().All(v => v);
            File.WriteAllText(Path.Combine(directory, "crt-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(checks["passed"] is true ? 0 : 1);
        }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(directory, "crt-error.txt"), ex.ToString());
            Application.Current.Shutdown(1);
        }
    }

    static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    sealed record PixelDelta(double ChangedFraction, double MeanChannelDelta);
    static PixelDelta PixelDifference(BitmapSource first, BitmapSource second)
    {
        if (first.PixelWidth != second.PixelWidth || first.PixelHeight != second.PixelHeight) throw new InvalidOperationException("Image dimensions differ.");
        int pixels = first.PixelWidth * first.PixelHeight, changed = 0;
        var a = new byte[pixels * 4]; var b = new byte[a.Length];
        first.CopyPixels(a, first.PixelWidth * 4, 0); second.CopyPixels(b, first.PixelWidth * 4, 0);
        long total = 0;
        for (int i = 0; i < a.Length; i += 4)
        {
            int maximum = 0;
            for (int c = 0; c < 3; c++) { int difference = Math.Abs(a[i + c] - b[i + c]); total += difference; maximum = Math.Max(maximum, difference); }
            if (maximum >= 4) changed++;
        }
        return new((double)changed / pixels, (double)total / (pixels * 3));
    }
}
