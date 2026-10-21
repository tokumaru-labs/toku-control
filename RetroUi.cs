using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shell;

namespace TokuControl;

public static class Retro
{
    public static SolidColorBrush Brush(string color) { var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); b.Freeze(); return b; }
    public static readonly Brush Background = Brush("#03070B"), Panel = Brush("#060C16"), Border = Brush("#6260BD"), Text = Brush("#C6CDD7"), Dim = Brush("#737C96"), Cyan = Brush("#00D9E7"), Green = Brush("#38E98B"), Amber = Brush("#DBCE49"), Red = Brush("#E96571");
    public static Brush Title => new LinearGradientBrush(Color.FromRgb(9, 46, 187), Color.FromRgb(56, 30, 126), 0);
    public static void Install(Application app)
    {
        var resources = (ResourceDictionary)XamlReader.Parse("""
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="Button">
            <Setter Property="Background" Value="#050A13"/><Setter Property="Foreground" Value="#C6CDD7"/>
            <Setter Property="BorderBrush" Value="#6260BD"/><Setter Property="BorderThickness" Value="1"/>
            <Setter Property="MinHeight" Value="26"/><Setter Property="Padding" Value="6,3"/><Setter Property="Margin" Value="3"/>
            <Setter Property="FontSize" Value="12"/><Setter Property="Cursor" Value="Hand"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button">
              <Border x:Name="edge" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1">
                <Border x:Name="inner" BorderBrush="#111D39" BorderThickness="0,0,1,1"><ContentPresenter Margin="{TemplateBinding Padding}" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
              </Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="edge" Property="BorderBrush" Value="#00D9E7"/><Setter Property="Foreground" Value="#E5FFFF"/></Trigger>
                <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="edge" Property="BorderBrush" Value="#DBCE49"/></Trigger>
                <Trigger Property="IsPressed" Value="True"><Setter TargetName="edge" Property="Background" Value="#142045"/><Setter TargetName="inner" Property="BorderThickness" Value="1,1,0,0"/></Trigger>
                <Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.36"/><Setter Property="Cursor" Value="Arrow"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="ComboBox">
            <Setter Property="Foreground" Value="#C6CDD7"/><Setter Property="Background" Value="#03070B"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="ComboBox">
              <Grid><Border Background="#03070B" BorderBrush="#6260BD" BorderThickness="1">
                <Grid><ContentPresenter Content="{TemplateBinding SelectionBoxItem}" Margin="6,2,24,2" VerticalAlignment="Center"/>
                  <ToggleButton IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}" HorizontalAlignment="Right" Width="22" Focusable="False"><ToggleButton.Template><ControlTemplate TargetType="ToggleButton"><Border Background="#0A1530" BorderBrush="#353E68" BorderThickness="1"><TextBlock Text="v" Foreground="#00D9E7" HorizontalAlignment="Center" VerticalAlignment="Center"/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>
                </Grid></Border>
                <Popup x:Name="PART_Popup" IsOpen="{TemplateBinding IsDropDownOpen}" Placement="Bottom" AllowsTransparency="False" Focusable="False">
                  <Border Background="#060C16" BorderBrush="#6260BD" BorderThickness="1" MinWidth="205"><ScrollViewer><ItemsPresenter/></ScrollViewer></Border>
                </Popup>
              </Grid>
              <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.5"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style TargetType="Slider">
            <Setter Property="Minimum" Value="0"/><Setter Property="Maximum" Value="100"/><Setter Property="Margin" Value="4"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
              <Grid Background="Transparent">
                <Border Background="#020407" BorderBrush="#353E68" BorderThickness="1" Height="6" VerticalAlignment="Center"/>
                <Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" Orientation="{TemplateBinding Orientation}">
                  <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                  <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
                  <Track.Thumb><Thumb Width="9" Height="17"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#C6CDD7" BorderBrush="#6260BD" BorderThickness="2,1,1,2"/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
                </Track>
              </Grid>
              <ControlTemplate.Triggers><Trigger Property="IsEnabled" Value="False"><Setter Property="Opacity" Value="0.4"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="VolumeStyle" TargetType="Slider">
            <Setter Property="Orientation" Value="Vertical"/><Setter Property="Minimum" Value="0"/><Setter Property="Maximum" Value="100"/>
            <Setter Property="SmallChange" Value="1"/><Setter Property="LargeChange" Value="2"/><Setter Property="IsMoveToPointEnabled" Value="True"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Slider">
              <Grid Background="Transparent"><Track x:Name="PART_Track" Minimum="{TemplateBinding Minimum}" Maximum="{TemplateBinding Maximum}" Value="{TemplateBinding Value}" Orientation="Vertical">
                <Track.DecreaseRepeatButton><RepeatButton Command="Slider.DecreaseLarge" Opacity="0" Focusable="False"/></Track.DecreaseRepeatButton>
                <Track.IncreaseRepeatButton><RepeatButton Command="Slider.IncreaseLarge" Opacity="0" Focusable="False"/></Track.IncreaseRepeatButton>
                <Track.Thumb><Thumb Width="48" Height="11"><Thumb.Template><ControlTemplate TargetType="Thumb"><Border Background="#172642" BorderBrush="#C6CDD7" BorderThickness="1"><Rectangle Height="2" Margin="7,0" Fill="#00D9E7"/></Border></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
              </Track></Grid>
              <ControlTemplate.Triggers><Trigger Property="IsKeyboardFocused" Value="True"><Setter Property="Background" Value="#172642"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
        </ResourceDictionary>
        """);
        app.Resources.MergedDictionaries.Add(resources);
    }
    public static TextBlock Label(string text, Brush? color = null, double size = 12) => new() { Text = text, Foreground = color ?? Text, FontSize = size, Margin = new Thickness(3), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, string hint, Action action)
    {
        var b = new Button { Content = text, ToolTip = hint }; AutomationProperties.SetName(b, text); b.Click += (_, _) => action(); return b;
    }
    public static StackPanel Row(params UIElement[] children)
    {
        var p = new StackPanel { Orientation = Orientation.Horizontal }; foreach (var c in children) p.Children.Add(c); return p;
    }
    public static StackPanel Col(params UIElement[] children)
    {
        var p = new StackPanel(); foreach (var c in children) p.Children.Add(c); return p;
    }
    public static Border Box(string title, UIElement child)
    {
        var dock = new DockPanel { LastChildFill = true };
        var head = new Border { Background = Title, BorderBrush = Border, BorderThickness = new Thickness(0, 0, 0, 1), Child = Label(title, Text, 12), Height = 24 };
        DockPanel.SetDock(head, Dock.Top); dock.Children.Add(head);
        dock.Children.Add(new Border { Padding = new Thickness(7), Child = child });
        return new Border { Background = Panel, BorderBrush = Border, BorderThickness = new Thickness(1), Margin = new Thickness(4), Child = dock };
    }
}

public class RetroWindow : Window
{
    public readonly Grid Surface = new();
    public readonly CrtOverlay Overlay = new();
    public readonly DockPanel Layout = new();
    public readonly TextBlock Footer = Retro.Label("LOCAL ONLY / READY", Retro.Dim, 10);
    public string Key { get; }
    public Action? OnPin;
    internal event Action? TitleDragStarted;
    internal event Action? TitleDragFinished;
    internal void BeginTitleDrag() => TitleDragStarted?.Invoke();
    internal void EndTitleDrag() => TitleDragFinished?.Invoke();
    readonly CrtDisplayEffect displayEffect = new();
    public RetroWindow(string key, string title, double width, double height, bool main = false)
    {
        Key = key; Title = title; Width = width; Height = height;
        Background = Retro.Background; Foreground = Retro.Text; FontFamily = new FontFamily("Lucida Console, MS Gothic"); FontSize = 12;
        WindowStyle = WindowStyle.None; ResizeMode = main ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
        MinWidth = main ? 720 : width; MinHeight = main ? 500 : height;
        UseLayoutRounding = true; SnapsToDevicePixels = true;
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.Aliased);
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 0, ResizeBorderThickness = new Thickness(main ? 5 : 0), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(0) });
        Surface.Background = Retro.Background;
        Content = new Border { BorderBrush = Retro.Border, Background = Retro.Background, BorderThickness = new Thickness(1), Child = Surface };
        Surface.Children.Add(Layout); Surface.Children.Add(Overlay); Panel.SetZIndex(Overlay, 100);
        Layout.SizeChanged += (_, _) => UpdateEffectPixelSize();
        var header = new DockPanel { Background = Retro.Title, Height = 28, LastChildFill = true };
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        var pin = Retro.Button(main ? "_" : "P", main ? "最小化" : "この小窓の最前面固定", () => { if (main) WindowState = WindowState.Minimized; else { Topmost = !Topmost; OnPin?.Invoke(); } });
        pin.MinHeight = 20; pin.Width = 24; pin.Margin = new Thickness(1, 3, 1, 3);
        var close = Retro.Button("x", main ? "アプリと全小窓を終了" : "小窓を閉じる", Close); close.MinHeight = 20; close.Width = 24; close.Margin = pin.Margin;
        controls.Children.Add(pin); controls.Children.Add(close); DockPanel.SetDock(controls, Dock.Right); header.Children.Add(controls);
        var name = Retro.Label("▣ " + title, Retro.Text, 13); name.Margin = new Thickness(7, 0, 0, 0); header.Children.Add(name);
        name.MouseLeftButtonDown += (_, e) =>
        {
            if (main && e.ClickCount == 2) WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            else if (e.LeftButton == MouseButtonState.Pressed)
            {
                BeginTitleDrag();
                try { DragMove(); }
                finally { EndTitleDrag(); }
            }
        };
        DockPanel.SetDock(header, Dock.Top); Layout.Children.Add(header);
        var foot = new Border { Child = Footer, BorderBrush = Retro.Border, BorderThickness = new Thickness(0, 1, 0, 0), Background = Retro.Brush("#0A1030"), MinHeight = 22 };
        DockPanel.SetDock(foot, Dock.Bottom); Layout.Children.Add(foot);
        if (!main) PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
    }
    public void Apply(Settings settings)
    {
        FontSize = settings.TextSize; Overlay.Apply(settings);
        displayEffect.Bloom = settings.Bloom * 3.2;
        displayEffect.RgbShift = 3 * Math.Pow(settings.Rgb / .5, 2);
        UpdateEffectPixelSize();
        Layout.Effect = settings.Crt && (settings.Bloom > 0 || settings.Rgb > 0) ? displayEffect : null;
    }
    void UpdateEffectPixelSize()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        displayEffect.TexelSize = new Point(1 / Math.Max(1, Layout.ActualWidth * dpi.DpiScaleX), 1 / Math.Max(1, Layout.ActualHeight * dpi.DpiScaleY));
    }
}

public sealed class CrtDisplayEffect : ShaderEffect
{
    static readonly PixelShader shader = LoadShader();
    static PixelShader LoadShader()
    {
        var value = new PixelShader { UriSource = new Uri("pack://application:,,,/TokuControl;component/Assets/CrtDisplay.ps", UriKind.Absolute) };
        value.Freeze(); return value;
    }
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(CrtDisplayEffect), 0);
    public static readonly DependencyProperty TexelSizeProperty = DependencyProperty.Register(nameof(TexelSize), typeof(Point), typeof(CrtDisplayEffect), new UIPropertyMetadata(new Point(1, 1), PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty BloomProperty = DependencyProperty.Register(nameof(Bloom), typeof(double), typeof(CrtDisplayEffect), new UIPropertyMetadata(0d, PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty RgbShiftProperty = DependencyProperty.Register(nameof(RgbShift), typeof(double), typeof(CrtDisplayEffect), new UIPropertyMetadata(0d, PixelShaderConstantCallback(2)));
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public Point TexelSize { get => (Point)GetValue(TexelSizeProperty); set => SetValue(TexelSizeProperty, value); }
    public double Bloom { get => (double)GetValue(BloomProperty); set => SetValue(BloomProperty, value); }
    public double RgbShift { get => (double)GetValue(RgbShiftProperty); set => SetValue(RgbShiftProperty, value); }
    public CrtDisplayEffect()
    {
        PixelShader = shader;
        UpdateShaderValue(InputProperty); UpdateShaderValue(TexelSizeProperty); UpdateShaderValue(BloomProperty); UpdateShaderValue(RgbShiftProperty);
    }
}

public sealed class CrtOverlay : FrameworkElement
{
    Settings settings = new();
    public CrtOverlay() { IsHitTestVisible = false; Focusable = false; }
    public void Apply(Settings value) { settings = value; InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        if (!settings.Crt) return;
        double scanline = .6 * Math.Pow(settings.Scanline / .18, 2);
        var lines = new Pen(new SolidColorBrush(Color.FromArgb((byte)(scanline * 255), 0, 0, 0)), 1); lines.Freeze();
        for (double y = 0; y < ActualHeight; y += 3) dc.DrawLine(lines, new Point(0, y), new Point(ActualWidth, y));
        var noise = new SolidColorBrush(Color.FromArgb((byte)(Math.Pow(settings.Noise / .08, 2) * .48 * 255), 160, 215, 255)); noise.Freeze();
        var rng = new Random(1999);
        for (int i = 0; i < ActualWidth * ActualHeight / 85; i++) dc.DrawRectangle(noise, null, new Rect(Math.Floor(rng.NextDouble() * ActualWidth), Math.Floor(rng.NextDouble() * ActualHeight), 1, 1));
        var vignette = new RadialGradientBrush { RadiusX = .68, RadiusY = .72 };
        vignette.GradientStops.Add(new GradientStop(Colors.Transparent, .35));
        vignette.GradientStops.Add(new GradientStop(Color.FromArgb((byte)(settings.Vignette / .35 * .65 * 255), 0, 0, 0), 1));
        dc.DrawRectangle(vignette, null, new Rect(0, 0, ActualWidth, ActualHeight));
    }
}

public sealed class LedMeter : FrameworkElement
{
    double level;
    public bool Vertical { get; set; }
    public Brush Color { get; set; } = Retro.Cyan;
    public double Level
    {
        get => level;
        set
        {
            double next = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0;
            int count = Vertical ? 26 : 42;
            bool changed = Math.Ceiling(level * count) != Math.Ceiling(next * count);
            level = next; if (changed) InvalidateVisual();
        }
    }
    protected override void OnRender(DrawingContext dc)
    {
        int count = Vertical ? 26 : 42; var dark = Retro.Brush("#102333");
        for (int i = 0; i < count; i++)
        {
            double w = Vertical ? ActualWidth : ActualWidth / count - 2;
            double h = Vertical ? ActualHeight / count - 2 : ActualHeight;
            var rect = new Rect(Vertical ? 0 : i * ActualWidth / count, Vertical ? ActualHeight - (i + 1) * ActualHeight / count : 0, Math.Max(0, w), Math.Max(0, h));
            dc.DrawRectangle(i < level * count ? (i > count * .9 ? Retro.Amber : Color) : dark, null, rect);
        }
    }
}

public sealed class HistoryGraph : FrameworkElement
{
    readonly Queue<double?> values = new();
    public Brush Color { get; set; } = Retro.Green;
    public int Count => values.Count;
    public void Add(double? value) { values.Enqueue(value); while (values.Count > 90) values.Dequeue(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRectangle(Retro.Brush("#020A0E"), new Pen(Retro.Brush("#183341"), 1), new Rect(0, 0, ActualWidth, ActualHeight));
        var grid = new Pen(Retro.Brush("#10242C"), 1);
        for (int i = 1; i < 8; i++) dc.DrawLine(grid, new Point(i * ActualWidth / 8, 0), new Point(i * ActualWidth / 8, ActualHeight));
        for (int i = 1; i < 4; i++) dc.DrawLine(grid, new Point(0, i * ActualHeight / 4), new Point(ActualWidth, i * ActualHeight / 4));
        var pen = new Pen(Color, 1); var array = values.ToArray();
        Point? previous = null;
        for (int i = 0; i < array.Length; i++)
        {
            if (!array[i].HasValue) { previous = null; continue; }
            var p = new Point((90 - array.Length + i) * ActualWidth / 89, (100 - array[i]!.Value) * (ActualHeight - 2) / 100 + 1);
            if (previous.HasValue) dc.DrawLine(pen, previous.Value, p); previous = p;
        }
    }
}

public sealed class SegmentClock : FrameworkElement
{
    static readonly int[] digits = [0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F];
    static readonly Geometry[] segments =
    [
        Polygon(new(4,4.5), new(8,1), new(31,1), new(35,4.5), new(31,8), new(8,8)),
        Polygon(new(34.5,6), new(38,10), new(38,25), new(34.5,29), new(31,25), new(31,10)),
        Polygon(new(34.5,35), new(38,39), new(38,54), new(34.5,58), new(31,54), new(31,39)),
        Polygon(new(4,60.5), new(8,57), new(31,57), new(35,60.5), new(31,64), new(8,64)),
        Polygon(new(4.5,35), new(8,39), new(8,54), new(4.5,58), new(1,54), new(1,39)),
        Polygon(new(4.5,6), new(8,10), new(8,25), new(4.5,29), new(1,25), new(1,10)),
        Polygon(new(4,31.5), new(8,28), new(31,28), new(35,31.5), new(31,35), new(8,35))
    ];
    static readonly Brush unlit = Retro.Brush("#071D17");
    static readonly Transform slant = new MatrixTransform(1, 0, -.075, 1, 4.8, 0);
    string time = "000000";
    static Geometry Polygon(params Point[] points)
    {
        var shape = new StreamGeometry();
        using (var context = shape.Open()) { context.BeginFigure(points[0], true, true); context.PolyLineTo(points.Skip(1).ToArray(), true, false); }
        shape.Freeze(); return shape;
    }
    public void SetTime(DateTime value) { time = value.ToString("HHmmss", CultureInfo.InvariantCulture); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth / 280, ActualHeight / 64);
        dc.PushTransform(new TranslateTransform((ActualWidth - 280 * scale) / 2, 0));
        dc.PushTransform(new ScaleTransform(scale, scale));
        double[] positions = [0, 44, 107, 151, 201, 233];
        for (int digit = 0; digit < time.Length; digit++)
        {
            double size = digit < 4 ? 1 : .72;
            dc.PushTransform(new TranslateTransform(positions[digit], 64 * (1 - size)));
            dc.PushTransform(new ScaleTransform(size, size)); dc.PushTransform(slant);
            int mask = digits[time[digit] - '0'];
            for (int i = 0; i < 7; i++) dc.DrawGeometry((mask & (1 << i)) != 0 ? Retro.Green : unlit, null, segments[i]);
            dc.Pop(); dc.Pop(); dc.Pop();
        }
        dc.DrawRectangle(Retro.Green, null, new Rect(96, 19, 6, 7));
        dc.DrawRectangle(Retro.Green, null, new Rect(94, 42, 6, 7));
        dc.Pop(); dc.Pop();
    }
}

public static class WindowPlacement
{
    [StructLayout(LayoutKind.Sequential)] struct RectI { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public RectI Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out RectI rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref RectI rect, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
    public static WindowPosition Read(RetroWindow window)
    {
        GetWindowRect(new WindowInteropHelper(window).Handle, out var r);
        var dpi = VisualTreeHelper.GetDpi(window);
        var size = window.WindowState == WindowState.Normal ? new Size((r.Right - r.Left) / dpi.DpiScaleX, (r.Bottom - r.Top) / dpi.DpiScaleY) : window.RestoreBounds.Size;
        return new(r.Left, r.Top, window.Topmost, size.Width, size.Height);
    }
    public static Rect NativeBounds(Window window)
    {
        GetWindowRect(new WindowInteropHelper(window).Handle, out var r);
        return new Rect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
    }
    public static Rect WorkArea(Window window)
    {
        GetWindowRect(new WindowInteropHelper(window).Handle, out var r);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        return GetMonitorInfo(MonitorFromRect(ref r, 2), ref info)
            ? new Rect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top) : Rect.Empty;
    }
    public static bool Tile(RetroWindow console, IReadOnlyList<RetroWindow> modules)
    {
        if (modules.Count == 0) return true;
        var work = WorkArea(console);
        if (console.WindowState != WindowState.Normal) console.WindowState = WindowState.Normal;
        foreach (var module in modules) if (module.WindowState != WindowState.Normal) module.WindowState = WindowState.Normal;
        var dpi = VisualTreeHelper.GetDpi(console); var main = NativeBounds(console);
        var sizes = modules.Select(w => (w.Key, new Size(Math.Ceiling(w.MinWidth * dpi.DpiScaleX), Math.Ceiling(w.MinHeight * dpi.DpiScaleY)))).ToArray();
        var plan = ModuleLayout.Calculate(work, main.TopLeft, main.Size, new Size(console.MinWidth * dpi.DpiScaleX, console.MinHeight * dpi.DpiScaleY), sizes, Math.Ceiling(12 * dpi.DpiScaleX));
        if (plan == null) return false;
        foreach (var window in modules.Prepend(console))
        {
            var frame = plan.Frames[window.Key];
            SetWindowPos(new WindowInteropHelper(window).Handle, IntPtr.Zero, (int)frame.X, (int)frame.Y, (int)frame.Width, (int)frame.Height, 0x0014);
        }
        return true;
    }
    public static void OverConsole(RetroWindow console, RetroWindow module)
    {
        var main = NativeBounds(console); var bounds = NativeBounds(module);
        var dpi = VisualTreeHelper.GetDpi(console);
        double width = bounds.Width > 0 ? bounds.Width : module.Width * dpi.DpiScaleX;
        double height = bounds.Height > 0 ? bounds.Height : module.Height * dpi.DpiScaleY;
        double x = main.X + Math.Max(0, main.Width - width - 20 * dpi.DpiScaleX);
        double offset = (module.Key == "clock" ? 45 : module.Key == "meter" ? 220 : 345) * dpi.DpiScaleY;
        double y = main.Y + Math.Clamp(offset, 0, Math.Max(0, main.Height - height - 20 * dpi.DpiScaleY));
        Place(module, new(x, y, module.Topmost));
    }
    public static void Place(RetroWindow window, WindowPosition? saved = null, int offset = 0)
    {
        if (saved?.Width is > 0) window.Width = Math.Max(window.MinWidth, saved.Width.Value);
        if (saved?.Height is > 0) window.Height = Math.Max(window.MinHeight, saved.Height.Value);
        var handle = new WindowInteropHelper(window).Handle; GetWindowRect(handle, out var current);
        int x = saved == null ? current.Left + offset : (int)Math.Clamp(saved.X, -100000, 100000);
        int y = saved == null ? current.Top + offset : (int)Math.Clamp(saved.Y, -100000, 100000);
        var desired = new RectI { Left = x, Top = y, Right = x + current.Right - current.Left, Bottom = y + current.Bottom - current.Top };
        var monitor = MonitorFromRect(ref desired, 2); var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info)) return;
        int width = Math.Min(current.Right - current.Left, info.Work.Right - info.Work.Left), height = Math.Min(current.Bottom - current.Top, info.Work.Bottom - info.Work.Top);
        x = Math.Clamp(x, info.Work.Left, info.Work.Right - width); y = Math.Clamp(y, info.Work.Top, info.Work.Bottom - height);
        SetWindowPos(handle, IntPtr.Zero, x, y, width, height, 0x0014); // preserve Z order and focus
        if (saved != null) window.Topmost = saved.Pin;
    }
}
