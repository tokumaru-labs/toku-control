using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace TokuControl;

// Keep the native Slider's keyboard focus and automation range, and render its values as a vertical drum.
internal sealed class MinuteWheel : Slider
{
    const int Row = 20;
    readonly TextBlock[] labels = new TextBlock[5];
    Canvas? face;
    double? downY;
    double downValue, offset;
    int wheelRemainder;
    TouchDevice? finger;
    bool dragged;
    static readonly ControlTemplate Drum = (ControlTemplate)XamlReader.Parse("""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Slider">
          <Border x:Name="edge" Background="#070E1A" BorderBrush="#40566B" BorderThickness="1">
            <Grid ClipToBounds="True">
              <Border Background="#17313B" Height="20" VerticalAlignment="Center"/>
              <Canvas x:Name="PART_Face" Background="Transparent" ClipToBounds="True"/>
            </Grid>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="edge" Property="BorderBrush" Value="#DBCE49"/></Trigger>
            <Trigger Property="IsEnabled" Value="False"><Setter TargetName="edge" Property="Opacity" Value=".65"/></Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
        """);
    public MinuteWheel()
    {
        Minimum = 5; Maximum = 120; SmallChange = TickFrequency = 5; LargeChange = 15;
        IsSnapToTickEnabled = true; Value = 25; Template = Drum;
        Width = 78; Height = 62; Margin = new Thickness(2);
        Focusable = true;
        ToolTip = "ホイール・上下スワイプ・↑↓キーで5分刻み";
        AutomationProperties.SetName(this, "タイマー時間（分）");
        SizeChanged += (_, _) => Draw();
    }
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        face = (Canvas)GetTemplateChild("PART_Face");
        face.Children.Clear();
        for (int i = 0; i < labels.Length; i++)
        {
            labels[i] = new TextBlock { Height = Row, TextAlignment = TextAlignment.Center, IsHitTestVisible = false };
            face.Children.Add(labels[i]);
        }
        face.SizeChanged += (_, _) => Draw();
        Draw();
    }
    protected override void OnValueChanged(double oldValue, double newValue)
    {
        double snapped = Math.Clamp(Math.Round(newValue / 5) * 5, Minimum, Maximum);
        if (snapped != newValue) { SetCurrentValue(ValueProperty, snapped); return; }
        base.OnValueChanged(oldValue, newValue);
        Draw();
    }
    void Draw()
    {
        if (face == null) return;
        for (int i = 0; i < labels.Length; i++)
        {
            int distance = i - 2, minutes = (int)Value + 5 * distance;
            var label = labels[i];
            label.Width = face.ActualWidth;
            label.Text = minutes >= Minimum && minutes <= Maximum ? minutes.ToString("00", CultureInfo.InvariantCulture) + " 分" : "";
            label.Foreground = distance == 0 ? Retro.Cyan : Retro.Dim;
            label.FontSize = distance == 0 ? 14 : 11;
            Canvas.SetTop(label, (face.ActualHeight - Row) / 2 + Row * distance + offset);
        }
    }
    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        wheelRemainder += e.Delta;
        int steps = wheelRemainder / Mouse.MouseWheelDeltaForOneLine;
        wheelRemainder %= Mouse.MouseWheelDeltaForOneLine;
        Value = Math.Clamp(Value - steps * 5, Minimum, Maximum);
        e.Handled = true;
    }
    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        double? next = e.Key switch
        {
            Key.Up or Key.Left => Value - 5, Key.Down or Key.Right => Value + 5,
            Key.PageUp => Value - 15, Key.PageDown => Value + 15, Key.Home => Minimum, Key.End => Maximum, _ => null
        };
        if (next.HasValue) { Value = Math.Clamp(next.Value, Minimum, Maximum); e.Handled = true; }
        else base.OnPreviewKeyDown(e);
    }
    void Begin(double y) { Focus(); downValue = Value; downY = y; dragged = false; offset = 0; wheelRemainder = 0; }
    void Move(double y)
    {
        if (downY is not double start) return;
        double delta = y - start;
        dragged |= Math.Abs(delta) >= SystemParameters.MinimumVerticalDragDistance;
        if (!dragged) return;
        double position = Math.Clamp(downValue - delta * 5 / Row, Minimum, Maximum);
        Value = Math.Round(position / 5, MidpointRounding.AwayFromZero) * 5;
        offset = (Value - position) * Row / 5; Draw();
    }
    void End(double y)
    {
        Move(y);
        if (downY.HasValue && !dragged)
        {
            if (y < ActualHeight / 2 - Row / 2) Value = Math.Max(Minimum, Value - 5);
            if (y > ActualHeight / 2 + Row / 2) Value = Math.Min(Maximum, Value + 5);
        }
        ClearGesture();
    }
    void ClearGesture() { downY = null; offset = 0; Draw(); }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (finger == null && CaptureMouse()) Begin(e.GetPosition(this).Y);
        e.Handled = true;
    }
    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured) { Move(e.GetPosition(this).Y); e.Handled = true; }
    }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (IsMouseCaptured) { End(e.GetPosition(this).Y); ReleaseMouseCapture(); }
        e.Handled = true;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { if (finger == null) ClearGesture(); base.OnLostMouseCapture(e); }
    protected override void OnPreviewTouchDown(TouchEventArgs e)
    {
        if (finger == null && CaptureTouch(e.TouchDevice)) { finger = e.TouchDevice; Begin(e.GetTouchPoint(this).Position.Y); }
        e.Handled = true;
    }
    protected override void OnPreviewTouchMove(TouchEventArgs e)
    {
        if (finger == e.TouchDevice) Move(e.GetTouchPoint(this).Position.Y);
        e.Handled = true;
    }
    protected override void OnPreviewTouchUp(TouchEventArgs e)
    {
        if (finger == e.TouchDevice) { End(e.GetTouchPoint(this).Position.Y); ReleaseTouchCapture(e.TouchDevice); }
        e.Handled = true;
    }
    protected override void OnLostTouchCapture(TouchEventArgs e)
    {
        if (finger == e.TouchDevice) { finger = null; ClearGesture(); }
        base.OnLostTouchCapture(e);
    }
}
