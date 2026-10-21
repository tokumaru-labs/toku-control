using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TokuControl;

public sealed partial class ConsoleWindow
{
    readonly Grid systemPanel = new();
    readonly TextBlock systemSummary = Retro.Label("READING WINDOWS / DISPLAYS...", Retro.Cyan, 9);
    readonly TextBlock systemReadStatus = Retro.Label("READING...", Retro.Dim, 9);
    readonly StackPanel pcDetails = new(), displayDetails = new();
    Border pcBox = null!, displayBox = null!;
    Button systemDetailsButton = null!, systemRefreshButton = null!;
    MachineSnapshot? machineSnapshot;
    Task? systemReadTask;

    UIElement BuildSystem()
    {
        Grid Line(string name, TextBlock value, HistoryGraph graph)
        {
            var line = new Grid { Height = 30 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) }); line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(47) });
            line.Children.Add(Retro.Label(name, Retro.Text, 10)); graph.Height = 23; graph.Margin = new Thickness(3, 3, 4, 3);
            Grid.SetColumn(graph, 1); line.Children.Add(graph); Grid.SetColumn(value, 2); line.Children.Add(value); return line;
        }
        systemPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        systemPanel.RowDefinitions.Add(new RowDefinition());
        systemPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        systemPanel.ColumnDefinitions.Add(new ColumnDefinition()); systemPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0) });
        var live = Retro.Col(Line("CPU", cpuValue, cpuGraph), Line("RAM", ramValue, ramGraph), Line("DISK\nUSED", diskValue, diskGraph));
        live.ToolTip = "システム全体 / 1秒更新 / 90秒履歴。DISK USEDはシステムドライブの容量使用率";
        systemPanel.Children.Add(live);
        Grid.SetRow(systemSummary, 1); systemPanel.Children.Add(systemSummary);
        systemDetailsButton = Retro.Button("SYSTEM / DISPLAYS >", "Windows・PC仕様・モニター情報を広く表示", () => SelectSection("system"));
        Grid.SetRow(systemDetailsButton, 2); systemPanel.Children.Add(systemDetailsButton);
        pcBox = Retro.Box("THIS PC / SYSTEM INFO", new ScrollViewer { Content = pcDetails, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        Grid.SetRow(pcBox, 1); Grid.SetRowSpan(pcBox, 2); systemPanel.Children.Add(pcBox);
        var monitorDock = new DockPanel();
        systemRefreshButton = Retro.Button("REFRESH INFO", "Windowsのモニター・システム情報を再取得（設定変更なし）", () => _ = RefreshSystemInfoAsync());
        var toolbar = Retro.Col(Retro.Row(systemRefreshButton), systemReadStatus);
        DockPanel.SetDock(toolbar, Dock.Bottom); monitorDock.Children.Add(toolbar);
        monitorDock.Children.Add(new ScrollViewer { Content = displayDetails, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        displayBox = Retro.Box("WINDOWS DISPLAYS / ACTIVE", monitorDock);
        Grid.SetColumn(displayBox, 1); Grid.SetRowSpan(displayBox, 3); systemPanel.Children.Add(displayBox);
        SetSystemExpanded(false); return systemPanel;
    }
    void SetSystemExpanded(bool expanded)
    {
        systemPanel.ColumnDefinitions[1].Width = expanded ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        pcBox.Visibility = displayBox.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        systemSummary.Visibility = systemDetailsButton.Visibility = expanded ? Visibility.Collapsed : Visibility.Visible;
    }
    Task RefreshSystemInfoAsync()
    {
        if (systemReadTask is { IsCompleted: false }) return systemReadTask;
        return systemReadTask = ReadAndRenderSystemInfo();
    }
    async Task ReadAndRenderSystemInfo()
    {
        systemRefreshButton.IsEnabled = false; systemReadStatus.Text = "READING WINDOWS...";
        try
        {
            var snapshot = await Task.Run(SystemInformation.Read);
            if (shuttingDown) return;
            machineSnapshot = snapshot; RenderSystemInfo(snapshot);
        }
        catch (Exception ex)
        {
            if (!shuttingDown)
            {
                systemReadStatus.Text = "READ FAILED / RETRY";
                if (machineSnapshot == null) systemSummary.Text = "INFO UNAVAILABLE / OPEN TO RETRY";
                Log("WARN", "system info read failed / " + ex.GetType().Name);
            }
        }
        finally { if (!shuttingDown) systemRefreshButton.IsEnabled = true; }
    }
    void RenderSystemInfo(MachineSnapshot info)
    {
        var primary = info.Monitors.FirstOrDefault(m => m.Primary);
        systemSummary.Text = $"{info.Monitors.Length} DISPLAY(S)" + (primary != null ? $" / MAIN {primary.Width}x{primary.Height}" : "") + "\n" + info.Windows;
        systemSummary.ToolTip = systemSummary.Text;
        pcDetails.Children.Clear(); displayDetails.Children.Clear();
        void Field(string title, string value)
        {
            var name = Retro.Label(title, Retro.Dim, 9); name.Margin = new Thickness(3, 5, 3, 0);
            pcDetails.Children.Add(name); pcDetails.Children.Add(Retro.Label(value, Retro.Text, 11));
        }
        Field("WINDOWS", info.Windows + "\nBUILD " + info.Build);
        Field("CPU", info.Cpu + $"\n{info.LogicalProcessors} LOGICAL PROCESSORS");
        Field("RAM", SystemInformation.Capacity(info.InstalledBytes) + " INSTALLED / " + SystemInformation.Capacity(info.UsableBytes) + " USABLE\n" + SystemInformation.Capacity(info.AvailableBytes) + " AVAILABLE AT REFRESH");
        Field("GRAPHICS / DISPLAY ADAPTERS", info.Graphics.Length > 0 ? string.Join("\n", info.Graphics) : "UNAVAILABLE");
        Field("SYSTEM DRIVE " + info.Drive, SystemInformation.Capacity(info.DriveTotal is >= 0 ? (ulong)info.DriveTotal : null) + " TOTAL / " + SystemInformation.Capacity(info.DriveFree is >= 0 ? (ulong)info.DriveFree : null) + " FREE AT REFRESH");
        Field("MODEL", info.Model);
        Field("UPTIME / AT REFRESH", $"{(int)info.Uptime.TotalDays} DAYS {info.Uptime.Hours:00}:{info.Uptime.Minutes:00}:{info.Uptime.Seconds:00}");
        if (info.Monitors.Length == 0) displayDetails.Children.Add(Retro.Label("NO ACTIVE DISPLAY DATA", Retro.Amber, 11));
        foreach (var monitor in info.Monitors)
        {
            string size = monitor.Width.HasValue && monitor.Height.HasValue ? $"{monitor.Width} x {monitor.Height}" : "N/A";
            string scale = monitor.Scale.HasValue ? monitor.Scale + "%" : "N/A";
            string position = monitor.X.HasValue && monitor.Y.HasValue ? $"({monitor.X}, {monitor.Y})" : "N/A";
            var card = Retro.Col(Retro.Label(monitor.Name, Retro.Green, 13),
                Retro.Label(monitor.Device.Replace(@"\\.\", "") + (monitor.Primary ? " / MAIN" : " / SECONDARY"), Retro.Cyan, 10),
                Retro.Label(size + " / " + SystemInformation.RefreshRate(monitor.Hertz), Retro.Text, 12),
                Retro.Label("SCALE " + scale + " / ROTATE " + monitor.Rotation + "\nDESKTOP " + position, Retro.Dim, 10));
            displayDetails.Children.Add(new Border { BorderBrush = Retro.Border, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(2, 3, 2, 7), Margin = new Thickness(0, 0, 3, 6), Child = card });
        }
        if (info.DisplayNotice.Length > 0) displayDetails.Children.Add(Retro.Label(info.DisplayNotice, Retro.Amber, 10));
        systemReadStatus.Text = "READ " + info.CapturedAt.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + " / REFRESH AFTER DISPLAY CHANGES";
    }

    async Task CheckSystemPanel(string directory, Dictionary<string, object> checks)
    {
        await RefreshSystemInfoAsync();
        systemDetailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); UpdateLayout();
        checks["systemDetailsNavigation"] = section == "system" && pcBox.IsVisible && displayBox.IsVisible && Grid.GetColumnSpan(panels["system"]) == 3 && Grid.GetRowSpan(panels["system"]) == 2;
        checks["systemSnapshotRendered"] = machineSnapshot != null && pcDetails.Children.Count > 0 && displayDetails.Children.Count >= machineSnapshot.Monitors.Length && systemRefreshButton.IsEnabled;
        checks["systemDetailColumnsSeparate"] = pcBox.TranslatePoint(new Point(pcBox.ActualWidth, 0), systemPanel).X <= displayBox.TranslatePoint(new Point(0, 0), systemPanel).X;
        checks["systemRefreshFits"] = systemRefreshButton.TranslatePoint(new Point(0, systemRefreshButton.ActualHeight), panels["system"]).Y <= panels["system"].ActualHeight - 7;
        var beforeRefresh = machineSnapshot;
        systemRefreshButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        if (systemReadTask != null) await systemReadTask;
        checks["systemRefreshButtonWorks"] = machineSnapshot != null && !ReferenceEquals(beforeRefresh, machineSnapshot) && systemRefreshButton.IsEnabled;
        Capture(this, Path.Combine(directory, "system-details.png"));
        var oldWidth = Width; Width = 800; UpdateLayout();
        checks["systemDetailsScrollableAtSmallWidth"] = displayBox.ActualWidth >= 300 && pcBox.ActualWidth >= 300;
        Capture(this, Path.Combine(directory, "system-details-small.png")); Width = oldWidth;
        SelectSection("local"); UpdateLayout();
        checks["systemOverviewRestored"] = systemDetailsButton.IsVisible && !displayBox.IsVisible && Grid.GetColumnSpan(panels["system"]) == 1 && Grid.GetRowSpan(panels["system"]) == 1;
        checks["systemOverviewFits"] = systemDetailsButton.TranslatePoint(new Point(0, systemDetailsButton.ActualHeight), panels["system"]).Y <= panels["system"].ActualHeight - 7;
        checks["systemPanelPassed"] = new[] { "systemDetailsNavigation", "systemSnapshotRendered", "systemDetailColumnsSeparate", "systemRefreshFits", "systemRefreshButtonWorks", "systemDetailsScrollableAtSmallWidth", "systemOverviewRestored", "systemOverviewFits" }.All(k => checks[k] is true);
    }
}
