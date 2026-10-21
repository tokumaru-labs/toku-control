using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace TokuControl;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test")) return Checks.Run(args);
        bool preview = args.Contains("--preview");
        string? Option(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        var crtCheck = Option("--crt-check-dir");
        if (crtCheck != null && (!preview || string.IsNullOrWhiteSpace(Option("--data-dir")))) return 2;
        var layoutCheck = Option("--layout-check-dir");
        if (layoutCheck != null && (!preview || string.IsNullOrWhiteSpace(Option("--data-dir")))) return 2;
        var gallery = Option("--gallery-dir");
        if (gallery != null && (!preview || string.IsNullOrWhiteSpace(Option("--data-dir")))) return 2;
        var app = new Application { ShutdownMode = ShutdownMode.OnMainWindowClose };
        Retro.Install(app);
        var data = Option("--data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TokumaruLabs", "TokuControl");
        var store = new SettingsStore(data);
        var main = new ConsoleWindow(store, preview);
        app.MainWindow = main;
        var qa = Option("--capture-dir");
        if (qa != null) main.Loaded += async (_, _) => await main.CaptureChecks(qa);
        var moduleCheck = Option("--module-check-dir");
        if (moduleCheck != null) main.Loaded += async (_, _) => await main.ModuleChecks(moduleCheck);
        if (crtCheck != null) main.Loaded += async (_, _) => await main.CrtChecks(crtCheck);
        if (layoutCheck != null) main.Loaded += async (_, _) => await main.LayoutChecks(layoutCheck);
        if (gallery != null) main.Loaded += async (_, _) => await main.CaptureGallery(gallery);
        return app.Run(main);
    }
}

public sealed partial class ConsoleWindow : RetroWindow
{
    readonly SettingsStore store;
    readonly Settings settings;
    readonly bool preview;
    readonly IAudioBackend audio;
    readonly IOutputRoutes routes;
    readonly OutputSwitcher outputSwitcher;
    PlaybackOutput[] outputs = [];
    readonly SystemProbe system = new();
    readonly RingLog logs = new();
    readonly Dictionary<string, RetroWindow> modules = new();
    WindowGroupDrag? groupDrag;
    readonly Dictionary<string, Border> panels = new();
    readonly Dictionary<string, Button> nav = new();
    readonly List<Button> audioButtons = new();
    readonly DispatcherTimer slowTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer audioTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly DispatcherTimer peakTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    readonly DispatcherTimer crtSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(200) };
    readonly Dictionary<string, (Slider Slider, TextBlock Value, Func<double> Read, double Maximum)> crtControls = new();
    TextBlock? crtStateLabel;
    bool syncingCrtControls;
    readonly Grid board = new();
    readonly TextBlock topTime = Retro.Label("", Retro.Green, 11);
    readonly TextBlock audioValue = Retro.Label("---%", Retro.Green, 24);
    readonly TextBlock audioStatus = Retro.Label("READING DEFAULT OUTPUT", Retro.Dim, 10);
    readonly TextBlock driveStatus = Retro.Label("SCANNING", Retro.Amber, 10);
    readonly TextBlock cpuValue = Retro.Label("N/A", Retro.Cyan, 11), ramValue = Retro.Label("N/A", Retro.Cyan, 11), diskValue = Retro.Label("N/A", Retro.Amber, 11);
    readonly HistoryGraph cpuGraph = new(), ramGraph = new() { Color = Retro.Cyan }, diskGraph = new() { Color = Retro.Amber };
    readonly LedMeter volumeLed = new() { Vertical = true, Width = 26, Height = 83 };
    readonly LedMeter leftPeak = new() { Width = 346, Height = 14 }, rightPeak = new() { Width = 346, Height = 14 };
    readonly TextBlock peakStatus = Retro.Label("PEAK / PRE-FADER", Retro.Dim, 10);
    readonly TextBlock leftPeakLabel = Retro.Label("L", Retro.Cyan);
    StackPanel? rightPeakRow;
    readonly Slider volumeSlider = new() { Width = 49, Height = 98, Style = (Style)Application.Current.Resources["VolumeStyle"] };
    readonly TextBox logView = new() { IsReadOnly = true, Background = Retro.Background, Foreground = Retro.Green, BorderThickness = new Thickness(0), FontSize = 11, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(5) };
    readonly ComboBox driveSelect = new() { MinWidth = 205, Height = 26, Margin = new Thickness(3), Foreground = Retro.Text };
    readonly ComboBox outputSelect = new() { Height = 24, Margin = new Thickness(3, 0, 3, 0), DisplayMemberPath = "Name", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Stretch, ClipToBounds = true };
    readonly TextBlock outputName = Retro.Label("OUTPUT / READING", Retro.Cyan, 10);
    Button setOutput = null!, scanOutputs = null!;
    Button eject = null!, closeDrive = null!, mute = null!, raw = null!, pause = null!;
    AudioState state = new(false, 0, false, "");
    SegmentClock? clock;
    TextBlock? clockDate;
    TextBlock? timerText;
    DateTimeOffset? timerEnd;
    bool updatingVolume, logPaused, shuttingDown, driveBusy, outputBusy, closeAfterSwitch;
    DateTime lastVolumeChange;
    bool volumePending;
    string section = "local";
    public long PeakSamples { get; private set; }
    public long SystemSamples { get; private set; }

    public ConsoleWindow(SettingsStore store, bool preview) : base("main", "TOKU CONTROL v0.2.1 -- LOCAL SYSTEM CONSOLE --", 1080, 740, true)
    {
        this.store = store; this.preview = preview; settings = store.Load();
        routes = preview ? new PreviewOutputRoutes() : new WindowsOutputRoutes();
        outputSwitcher = new(routes);
        audio = preview ? new PreviewAudio((PreviewOutputRoutes)routes) : new CoreAudio();
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var headerGrid = new Grid { Margin = new Thickness(9, 5, 9, 6) };
        headerGrid.ColumnDefinitions.Add(new ColumnDefinition()); headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        var head = Retro.Col(Retro.Label("HOST: [ LOCAL ]   SCOPE: THIS PC   MODE: STANDBY", Retro.Text, 11), Retro.Label("OS: WINDOWS    PORT: -- / NOT USED    SESSION: 1", Retro.Dim, 11));
        headerGrid.Children.Add(head); Grid.SetColumn(topTime, 1); headerGrid.Children.Add(topTime);
        DockPanel.SetDock(headerGrid, Dock.Top); Layout.Children.Add(headerGrid);
        if (preview)
        {
            var notice = Retro.Label("PREVIEW / SIMULATED AUDIO -- OS CONTROL NOT CONNECTED", Retro.Amber, 12);
            DockPanel.SetDock(notice, Dock.Top); Layout.Children.Add(notice);
        }
        var logPanel = BuildLog(); DockPanel.SetDock(logPanel, Dock.Bottom); Layout.Children.Add(logPanel);
        var middle = new Grid { Margin = new Thickness(5, 0, 5, 2) };
        middle.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(157) }); middle.ColumnDefinitions.Add(new ColumnDefinition());
        middle.Children.Add(BuildNavigation());
        board.ColumnDefinitions.Add(new ColumnDefinition()); board.ColumnDefinitions.Add(new ColumnDefinition()); board.ColumnDefinitions.Add(new ColumnDefinition());
        board.RowDefinitions.Add(new RowDefinition { Height = new GridLength(242) }); board.RowDefinitions.Add(new RowDefinition { Height = new GridLength(203) });
        board.MinWidth = 810;
        AddPanel("devices", "DRIVE CONTROL", BuildDrive(), 0, 0);
        AddPanel("audio", "AUDIO CONTROL", BuildAudio(), 0, 1);
        AddPanel("tools", "LOCAL TOOLS", BuildTools(), 0, 2);
        AddPanel("system", "SYSTEM PULSE", BuildSystem(), 1, 0);
        AddPanel("launcher", "QUICK LAUNCH", BuildLaunch(), 1, 1);
        AddPanel("modules", "MODULE CONTROL", BuildModules(), 1, 2);
        var scroll = new ScrollViewer { Content = board, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(scroll, 1); middle.Children.Add(scroll); Layout.Children.Add(middle);
        Footer.Text = "READY.     LOCAL ONLY                        CRT: ON / SOUND: OFF";
        AddHandler(Mouse.MouseEnterEvent, new MouseEventHandler((_, e) => Hint(e.OriginalSource as DependencyObject)), true);
        AddHandler(Keyboard.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler((_, e) => Hint(e.NewFocus as DependencyObject)), true);
        ApplyAll();
        SourceInitialized += (_, _) => groupDrag = new(this, () => modules.Values, (moved, success) =>
        {
            settings.Windows["main"] = WindowPlacement.Read(this);
            foreach (var window in moved) settings.Windows[window.Key] = WindowPlacement.Read(window);
            Save();
            if (!success) { Footer.Text = "WINDOW MOVE INCOMPLETE / USE SHOW ALL"; Log("WARN", "some modules could not follow the console"); }
        });
        Loaded += (_, _) =>
        {
            settings.Windows.TryGetValue("main", out var pos); WindowPlacement.Place(this, pos);
            Log("LOCAL", preview ? "preview console ready; OS writes disabled" : "console ready / local services only");
            if (store.Warning != null) Log("WARN", store.Warning);
            ScanOutputs(); SyncAudio(); ScanDrives(); Tick(); slowTimer.Start(); audioTimer.Start(); peakTimer.Start();
            _ = RefreshSystemInfoAsync();
            ShowModule("clock", false); ShowModule("meter", false);
        };
        slowTimer.Tick += (_, _) => Tick(); audioTimer.Tick += (_, _) => { if (AudioVisible()) SyncAudio(); };
        peakTimer.Tick += (_, _) => UpdatePeaks();
        crtSaveTimer.Tick += (_, _) => CommitCrtChanges();
        Closing += (_, e) =>
        {
            if (outputBusy) { e.Cancel = true; closeAfterSwitch = true; Footer.Text = "WAIT / FINISHING OUTPUT SWITCH"; return; }
            if (shuttingDown) return; shuttingDown = true;
            groupDrag?.Dispose(); groupDrag = null;
            slowTimer.Stop(); audioTimer.Stop(); peakTimer.Stop(); crtSaveTimer.Stop();
            settings.Windows["main"] = WindowPlacement.Read(this);
            foreach (var window in modules.Values.ToArray()) window.Close();
            audio.Dispose(); routes.Dispose(); var error = store.Save(settings);
            if (error != null) MessageBox.Show(error + "\n" + store.FilePath, "TOKU CONTROL", MessageBoxButton.OK, MessageBoxImage.Warning);
        };
    }

    void Hint(DependencyObject? element)
    {
        while (element != null)
        {
            if (element is FrameworkElement fe && fe.ToolTip is string hint) { Footer.Text = hint; return; }
            element = element is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(element) : (element as FrameworkContentElement)?.Parent;
        }
    }
    void AddPanel(string key, string name, UIElement content, int row, int col)
    {
        var panel = Retro.Box(name, content); Grid.SetRow(panel, row); Grid.SetColumn(panel, col); board.Children.Add(panel); panels.Add(key, panel);
    }
    Border BuildNavigation()
    {
        var p = new StackPanel { Margin = new Thickness(4) };
        foreach (var (key, name) in new[] { ("local", "local options"), ("devices", "devices"), ("audio", "audio"), ("clock", "clock module"), ("system", "system pulse"), ("launcher", "launcher"), ("modules", "modules"), ("tools", "local tools"), ("settings", "CRT settings") })
        {
            var b = Retro.Button("+ " + name, name + " を表示", () => SelectSection(key));
            b.HorizontalContentAlignment = HorizontalAlignment.Left; b.Margin = new Thickness(0, 2, 0, 2); b.BorderThickness = new Thickness(0); b.Height = 29;
            nav.Add(key, b); p.Children.Add(b);
        }
        p.Children.Add(Retro.Label("──────────────", Retro.Dim, 10));
        p.Children.Add(Retro.Label("[ LOCAL BUS ]\n\nNO REMOTE LINK\nNO TELEMETRY\n\nOUTPUT / RENDER\nROLE / MULTIMEDIA", Retro.Dim, 9));
        nav["local"].Background = Retro.Title;
        return new Border { BorderBrush = Retro.Border, BorderThickness = new Thickness(1), Margin = new Thickness(4), Child = p };
    }
    void SelectSection(string key)
    {
        if (key is "clock" or "settings") { ShowModule(key == "clock" ? "clock" : "crt"); return; }
        section = key;
        foreach (var entry in nav) entry.Value.Background = entry.Key == key ? Retro.Title : Retro.Background;
        foreach (var entry in panels)
        {
            Grid.SetRowSpan(entry.Value, 1);
            entry.Value.Visibility = key == "local" || entry.Key == key ? Visibility.Visible : Visibility.Collapsed;
            if (key != "local" && entry.Key == key) { Grid.SetRow(entry.Value, 0); Grid.SetColumn(entry.Value, 0); Grid.SetColumnSpan(entry.Value, 2); }
            else
            {
                int index = panels.Keys.ToList().IndexOf(entry.Key); Grid.SetRow(entry.Value, index / 3); Grid.SetColumn(entry.Value, index % 3); Grid.SetColumnSpan(entry.Value, 1);
            }
        }
        if (key == "system") { Grid.SetColumnSpan(panels["system"], 3); Grid.SetRowSpan(panels["system"], 2); }
        SetSystemExpanded(key == "system");
        if (key == "system" && (machineSnapshot == null || DateTime.Now - machineSnapshot.CapturedAt > TimeSpan.FromSeconds(30))) _ = RefreshSystemInfoAsync();
        Footer.Text = key.ToUpperInvariant() + " / LOCAL VIEW";
    }
    UIElement BuildDrive()
    {
        eject = Retro.Button("EJECT", "選択した光学ドライブのトレイを開く", () => _ = DriveCommand(true));
        closeDrive = Retro.Button("CLOSE", "対応ドライブに閉じるコマンドを送る（機種依存）", () => _ = DriveCommand(false));
        return Retro.Col(Retro.Label("OPTICAL DEVICE / LOCAL", Retro.Dim, 10), driveSelect,
            Retro.Row(eject, closeDrive, Retro.Button("SCAN", "光学ドライブを再検出", ScanDrives)), driveStatus,
            Retro.Label("MEDIA STATE : NOT QUERIED\nAUTO CLOSE  : OFF\nACCESS      : USER", Retro.Dim, 10),
            Retro.Label("[ i ] CD / DVD DEVICES ONLY", Retro.Cyan, 10));
    }
    UIElement BuildAudio()
    {
        volumeSlider.ToolTip = "既定の再生音量。ドラッグ／矢印・ホイール1%、UP/DOWN 2%"; AutomationProperties.SetName(volumeSlider, "Master volume");
        var fader = new Grid { Width = 63, Height = 100, Margin = new Thickness(6, 0, 5, 0) }; fader.Children.Add(volumeLed); fader.Children.Add(volumeSlider);
        mute = AudioButton("MUTE", "音量を保持してミュートを切り替える", ToggleMute);
        volumeSlider.ValueChanged += (_, _) => { if (!updatingVolume && state.Available) SetVolume((float)volumeSlider.Value / 100); };
        volumeSlider.PreviewMouseWheel += (_, e) => { if (state.Available) SetVolume(state.Volume + Math.Sign(e.Delta) * .01f); e.Handled = true; };
        volumeSlider.PreviewKeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Up or System.Windows.Input.Key.Right or System.Windows.Input.Key.Down or System.Windows.Input.Key.Left)
            { SetVolume(state.Volume + (e.Key is System.Windows.Input.Key.Up or System.Windows.Input.Key.Right ? .01f : -.01f)); e.Handled = true; }
        };
        var right = Retro.Col(Retro.Row(Retro.Label("MASTER", Retro.Text, 10), audioValue),
            Retro.Row(AudioButton("UP", "音量を2ポイント上げる", () => SetVolume(state.Volume + .02f)), AudioButton("DOWN", "音量を2ポイント下げる", () => SetVolume(state.Volume - .02f))),
            Retro.Row(mute, Retro.Button("METER", "実測ピークの小窓を開く", () => ShowModule("meter"))));
        outputName.TextTrimming = TextTrimming.CharacterEllipsis; outputName.TextWrapping = TextWrapping.NoWrap;
        outputSelect.ToolTip = "出力を選び SET OUTPUT で確定。選ぶだけでは切り替えません";
        AutomationProperties.SetName(outputSelect, "Playback output");
        setOutput = Retro.Button("SET OUTPUT", "通常再生の既定出力（Console / Multimedia）を切り替えます。通話用と音量は保持", () => _ = SwitchOutput());
        scanOutputs = Retro.Button("SCAN", "使用可能な再生出力を再検出", ScanOutputs);
        return Retro.Col(Retro.Row(fader, right), audioStatus, outputName, outputSelect, Retro.Row(setOutput, scanOutputs));
    }
    UIElement BuildTools()
    {
        raw = Retro.Button("RAW / CRT", "CRTの質感をON/OFF", () => { settings.Crt = !settings.Crt; ApplyAll(); Save(); Log("OK", settings.Crt ? "CRT enabled" : "RAW display / CRT disabled"); });
        var p = Retro.Col(Retro.Row(raw, Retro.Button("DIM", "このアプリの発光を弱める", () => { settings.Bloom = .06; ApplyAll(); Save(); Log("OK", "phosphor dimmed"); })),
            Retro.Row(Retro.Button("CRT SET", "CRTの詳細設定を開く", () => ShowModule("crt")), Retro.Button("RESTORE", "アプリのCRT設定だけ初期値へ戻す", RestoreCrt)),
            Retro.Row(Retro.Button("RECALL", "全小窓を呼び戻して右側に縦積み", () => Arrange(true)), Retro.Button("GRID", "右側で幅と上下端をそろえて縦に整列", () => Arrange(true))),
            Retro.Label("RENDER  / PIXEL ALIGNED\nFX BUS  / WINDOW ONLY\nSOUND   / OFF", Retro.Dim, 10), Retro.Label("[■] LOCAL CONTROL READY", Retro.Green, 10));
        return p;
    }
    UIElement BuildLaunch()
    {
        var p = Retro.Col(Retro.Label("FIXED LOCAL TARGETS", Retro.Dim, 10));
        foreach (string target in new[] { "EXPLORER", "NOTEPAD", "CALC" })
            p.Children.Add(Retro.Button(target, target + " を起動", () => Launch(target)));
        p.Children.Add(Retro.Label("NO SHELL INPUT\nNO REMOTE SCRIPTS", Retro.Dim, 9)); return p;
    }
    UIElement BuildModules() => Retro.Col(
        Retro.Row(Retro.Button("SHOW CLOCK", "時計の独立小窓を表示", () => ShowModule("clock")), Retro.Button("PIN ALL", "自分の小窓だけ最前面固定", () => { foreach (var w in modules.Values) w.Topmost = true; SavePositions(); Log("OK", "modules pinned"); })),
        Retro.Row(Retro.Button("SPAWN METER", "音量ピークの独立小窓を表示", () => ShowModule("meter")), Retro.Button("UNPIN", "小窓の最前面固定を解除", () => { foreach (var w in modules.Values) w.Topmost = false; SavePositions(); Log("OK", "modules unpinned"); })),
        Retro.Row(Retro.Button("SHOW ALL", "3つの小窓を右側に縦積みして表示", () => Arrange(true)), Retro.Button("HIDE ALL", "小窓を非表示（再表示可能）", () => { foreach (var w in modules.Values) w.Hide(); Log("OK", "modules hidden"); })),
        Retro.Row(Retro.Button("RECALL", "全小窓を呼び戻して右側に縦積み", () => Arrange(true)), Retro.Button("GRID", "右側で幅と上下端をそろえて縦に整列", () => Arrange(true))),
        Retro.Label("CLOCK / AUDIO / CRT\nP = PIN     x = CLOSE", Retro.Dim, 9));
    Border BuildLog()
    {
        var g = new Grid { Height = 126 }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
        AutomationProperties.SetName(logView, "Event log"); g.Children.Add(logView);
        pause = Retro.Button("PAUSE", "ログの自動追従だけ停止。収集は続く", () => { logPaused = !logPaused; pause.Content = logPaused ? "FOLLOW" : "PAUSE"; if (!logPaused) logView.ScrollToEnd(); });
        var p = Retro.Col(Retro.Button("CLEAR", "画面内のログだけ消去", () => { logs.Clear(); RefreshLog(); }), Retro.Button("SAVE", "選択した場所へログを保存", SaveLog), pause);
        Grid.SetColumn(p, 1); g.Children.Add(p); return Retro.Box("EVENT LOG / LOCAL OPERATIONS", g);
    }
    void Launch(string target)
    {
        try
        {
            if (preview) { Log("INFO", "preview / launcher disabled"); return; }
            string executable = target == "EXPLORER" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") : Path.Combine(Environment.SystemDirectory, target == "NOTEPAD" ? "notepad.exe" : "calc.exe");
            Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true }); Log("OK", target.ToLowerInvariant() + " launch request accepted");
        }
        catch (Exception ex) { Log("ERROR", "launch failed / " + ex.GetType().Name); }
    }
    void ScanDrives()
    {
        if (driveBusy) return;
        try
        {
            var drives = preview ? Array.Empty<string>() : OpticalDrives.Scan();
            var old = driveSelect.SelectedItem as string; driveSelect.ItemsSource = drives;
            driveSelect.SelectedItem = old != null && drives.Contains(old) ? old : drives.FirstOrDefault();
            eject.IsEnabled = closeDrive.IsEnabled = drives.Length > 0;
            driveSelect.IsEnabled = drives.Length > 0;
            driveStatus.Text = preview ? "PREVIEW / DRIVE DISCONNECTED" : drives.Length == 0 ? "NO OPTICAL DRIVE" : "READY / TRAY STATE UNKNOWN";
            Log("INFO", preview ? "preview / optical commands disabled" : drives.Length == 0 ? "no optical drive detected" : $"{drives.Length} optical device(s) detected");
        }
        catch (Exception ex) { eject.IsEnabled = closeDrive.IsEnabled = false; driveStatus.Text = "UNAVAILABLE"; Log("ERROR", "drive scan / " + ex.GetType().Name); }
    }
    async Task DriveCommand(bool open)
    {
        if (preview || driveBusy || driveSelect.SelectedItem is not string root) return;
        driveBusy = true; eject.IsEnabled = closeDrive.IsEnabled = driveSelect.IsEnabled = false; driveStatus.Text = "BUSY / COMMAND IN PROGRESS";
        var result = await OpticalDrives.Command(root, open);
        if (shuttingDown) return;
        driveStatus.Text = result; Log(result.StartsWith("COMMAND ACCEPTED") ? "INFO" : "WARN", root + " " + (open ? "EJECT" : "CLOSE") + " / " + result);
        driveBusy = false; eject.IsEnabled = closeDrive.IsEnabled = driveSelect.IsEnabled = true;
    }
    bool MainVisible() => IsVisible && WindowState != WindowState.Minimized;
    void ScanOutputs()
    {
        if (outputBusy) return;
        try
        {
            var selected = (outputSelect.SelectedItem as PlaybackOutput)?.Id;
            outputs = routes.List(); outputSelect.ItemsSource = outputs;
            string current; try { current = routes.Default(1); } catch { current = ""; }
            outputSelect.SelectedItem = outputs.FirstOrDefault(x => x.Id == selected) ?? outputs.FirstOrDefault(x => x.Id == current) ?? outputs.FirstOrDefault();
            outputSelect.IsEnabled = setOutput.IsEnabled = outputs.Length > 0;
            UpdateOutputName(current);
        }
        catch (Exception ex)
        {
            outputs = []; outputSelect.ItemsSource = outputs; outputSelect.IsEnabled = setOutput.IsEnabled = false;
            outputName.Text = "OUTPUT LIST UNAVAILABLE"; Log("WARN", $"output scan / 0x{ex.HResult:X8}");
        }
    }
    void UpdateOutputName(string id)
    {
        var name = outputs.FirstOrDefault(x => x.Id == id)?.Name ?? (id.Length == 0 ? "NO DEFAULT OUTPUT" : "OUTPUT CHANGED / SCAN");
        outputName.Text = name; outputName.ToolTip = name + "\n" + id;
    }
    async Task SwitchOutput()
    {
        if (outputBusy || outputSelect.SelectedItem is not PlaybackOutput target) return;
        outputBusy = true; setOutput.IsEnabled = scanOutputs.IsEnabled = outputSelect.IsEnabled = false;
        SetAudioUi(state); outputName.Text = "SWITCHING / " + target.Name;
        var result = await outputSwitcher.Switch(target.Id);
        outputBusy = false; scanOutputs.IsEnabled = true;
        ScanOutputs(); SyncAudio(); Log(result.Success ? (preview ? "SIM" : "OK") : "WARN", result.Message);
        if (closeAfterSwitch) Close();
    }
    bool AudioVisible() => MainVisible() || modules.TryGetValue("meter", out var w) && w.IsVisible && w.WindowState != WindowState.Minimized;
    void SyncAudio()
    {
        var next = audio.Read();
        if (next.Available != state.Available || next.Device != state.Device) Log(next.Available ? "INFO" : "WARN", next.Available ? "default render endpoint synchronized" : next.Error);
        SetAudioUi(next);
        if (!outputBusy) UpdateOutputName(next.Device);
    }
    void SetAudioUi(AudioState next)
    {
        state = next; updatingVolume = true; volumeSlider.Value = next.Volume * 100; updatingVolume = false;
        volumeSlider.IsEnabled = mute.IsEnabled = next.Available && !outputBusy; volumeLed.Level = next.Available ? next.Volume : 0;
        foreach (var b in audioButtons) b.IsEnabled = next.Available && !outputBusy;
        audioValue.Text = next.Available ? Math.Round(next.Volume * 100).ToString("0") + "%" : "N/A";
        audioStatus.Text = !next.Available ? next.Error : next.Muted ? "MUTED / LEVEL PRESERVED" : "DEFAULT PLAYBACK / ACTIVE";
        audioStatus.ToolTip = next.Device; mute.Content = next.Muted ? "UNMUTE" : "MUTE";
        peakStatus.Text = preview ? "PREVIEW / SIMULATED AUDIO" : next.Muted ? "MUTED / PEAK IS PRE-FADER" : "PEAK / PRE-FADER / NOT FFT";
    }
    void SetVolume(float value)
    {
        if (!state.Available || outputBusy) return;
        var result = audio.SetVolume(Math.Clamp(value, 0, 1)); SetAudioUi(result);
        if (result.Error.Length > 0) { Log("WARN", result.Error); volumePending = false; }
        else { volumePending = true; lastVolumeChange = DateTime.UtcNow; }
    }
    Button AudioButton(string text, string hint, Action action)
    {
        var b = Retro.Button(text, hint, action); b.IsEnabled = state.Available; audioButtons.Add(b); return b;
    }
    void ToggleMute()
    {
        if (!state.Available || outputBusy) return;
        var result = audio.ToggleMute(); SetAudioUi(result);
        Log(result.Error.Length == 0 ? (preview ? "SIM" : "OK") : "WARN", result.Error.Length > 0 ? result.Error : result.Muted ? "master muted / volume preserved" : "master unmuted");
    }
    void UpdatePeaks()
    {
        if (!modules.TryGetValue("meter", out var w) || !w.IsVisible || w.WindowState == WindowState.Minimized) return;
        var peaks = audio.Peaks(); PeakSamples++;
        leftPeak.Level = peaks.Length > 0 ? peaks[0] : 0; rightPeak.Level = peaks.Length > 1 ? peaks[1] : 0;
        leftPeakLabel.Text = peaks.Length == 1 ? "M" : "L";
        if (rightPeakRow != null) rightPeakRow.Visibility = peaks.Length > 1 ? Visibility.Visible : Visibility.Collapsed;
        if (peaks.Length == 0) peakStatus.Text = "PEAK UNAVAILABLE / NO SYNTHETIC DATA";
        else if (!preview) peakStatus.Text = (state.Muted ? "MUTED / " : "") + (peaks.Length == 2 ? "L / R" : "MONO") + " / PRE-FADER / NOT FFT";
    }
    void Tick()
    {
        var now = DateTime.Now;
        if (MainVisible())
        {
            topTime.Text = now.ToString("yyyy.MM.dd  HH:mm:ss\n", CultureInfo.InvariantCulture) + "■ LOCAL ACTIVE";
            var p = system.Read(); SystemSamples++;
            UpdateMetric(cpuValue, cpuGraph, p.Cpu); UpdateMetric(ramValue, ramGraph, p.Ram); UpdateMetric(diskValue, diskGraph, p.Disk);
        }
        if (modules.TryGetValue("clock", out var cw) && cw.IsVisible)
        {
            clock?.SetTime(now); if (clockDate != null) clockDate.Text = now.ToString("yyyy.MM.dd  ddd", CultureInfo.InvariantCulture).ToUpperInvariant();
            if (timerText != null) timerText.Text = timerEnd == null ? "TIMER / IDLE" : DateTimeOffset.UtcNow >= timerEnd ? "TIMER COMPLETE / SILENT" : "REMAIN / " + (timerEnd.Value - DateTimeOffset.UtcNow).ToString(@"hh\:mm\:ss");
        }
        if (volumePending && DateTime.UtcNow - lastVolumeChange > TimeSpan.FromMilliseconds(700))
        { volumePending = false; Log(preview ? "SIM" : "OK", $"master volume: {Math.Round(state.Volume * 100)}%"); }
    }
    static void UpdateMetric(TextBlock label, HistoryGraph graph, double? value) { label.Text = value.HasValue ? value.Value.ToString("0") + "%" : "N/A"; graph.Add(value); }
    void Log(string level, string message) { logs.Add(level, message); RefreshLog(); }
    void RefreshLog() { var offset = logView.VerticalOffset; logView.Text = string.Join(Environment.NewLine, logs.Lines); if (!logPaused) logView.ScrollToEnd(); else logView.ScrollToVerticalOffset(offset); }
    void SaveLog()
    {
        var dialog = new SaveFileDialog { FileName = "toku-control-log.txt", Filter = "Text log (*.txt)|*.txt", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllLines(dialog.FileName, logs.Lines); Log("OK", "event log saved"); } catch (Exception ex) { Log("ERROR", "log save failed / " + ex.GetType().Name); }
    }
    void Save() { var error = store.Save(settings); if (error != null) Log("WARN", error); }
    void SavePositions() { settings.Windows["main"] = WindowPlacement.Read(this); foreach (var w in modules.Values) settings.Windows[w.Key] = WindowPlacement.Read(w); Save(); }

    void RevealModule(RetroWindow window)
    {
        if (window.WindowState != WindowState.Normal) window.WindowState = WindowState.Normal;
        window.Show();
        // Explicit calls keep visible modules beside the console. Startup still
        // restores saved positions, including deliberate multi-monitor layouts.
        Arrange(false);
        window.Activate();
    }
    void ShowModule(string key, bool reveal = true)
    {
        if (modules.TryGetValue(key, out var existing))
        {
            if (reveal) RevealModule(existing);
            else { existing.Show(); WindowPlacement.Place(existing, WindowPlacement.Read(existing)); }
            Log("INFO", key + " module shown"); return;
        }
        RetroWindow w;
        if (key == "clock")
        {
            w = new("clock", "CLOCK MODULE", 420, 212); clock = new SegmentClock { Width = 336, Height = 76, Margin = new Thickness(5, 6, 5, 2) };
            clockDate = Retro.Label("", Retro.Green, 12); clockDate.HorizontalAlignment = HorizontalAlignment.Center;
            timerText = Retro.Label("TIMER / IDLE", Retro.Dim, 10); timerText.HorizontalAlignment = HorizontalAlignment.Center;
            void Timer(int minutes) { timerEnd = minutes == 0 ? null : DateTimeOffset.UtcNow.AddMinutes(minutes); Tick(); Log("INFO", minutes == 0 ? "timer reset" : $"silent timer set / {minutes} min"); }
            var clockButtons = Retro.Row(Retro.Button("25 MIN", "25分の無音タイマー", () => Timer(25)), Retro.Button("50 MIN", "50分の無音タイマー", () => Timer(50)), Retro.Button("RESET", "タイマーを解除", () => Timer(0)), Retro.Button("PIN", "時計だけ最前面固定", () => { w.Topmost = !w.Topmost; SavePositions(); }));
            clockButtons.HorizontalAlignment = HorizontalAlignment.Center;
            w.Layout.Children.Add(Retro.Col(clock, clockDate, clockButtons, timerText));
            w.Footer.Text = "LOCAL TIME / 24H / SILENT TIMER";
        }
        else if (key == "meter")
        {
            w = new("meter", "AUDIO METER", 420, 182);
            // WPF elements can have one parent; detach from a previously closed module.
            if (leftPeak.Parent is Panel lp) lp.Children.Remove(leftPeak);
            if (leftPeakLabel.Parent is Panel llp) llp.Children.Remove(leftPeakLabel);
            if (rightPeak.Parent is Panel rp) rp.Children.Remove(rightPeak);
            if (peakStatus.Parent is Panel sp) sp.Children.Remove(peakStatus);
            rightPeakRow = Retro.Row(Retro.Label("R", Retro.Cyan), rightPeak);
            var meterButtons = Retro.Row(AudioButton("MUTE", "マスター音量を保持してミュート切替", ToggleMute), AudioButton("-", "マスター音量を2ポイント下げる", () => SetVolume(state.Volume - .02f)), AudioButton("+", "マスター音量を2ポイント上げる", () => SetVolume(state.Volume + .02f)), Retro.Button("PIN", "メーターだけ最前面固定", () => { w.Topmost = !w.Topmost; SavePositions(); }));
            meterButtons.HorizontalAlignment = HorizontalAlignment.Center;
            var meters = Retro.Col(Retro.Row(leftPeakLabel, leftPeak), rightPeakRow, peakStatus, meterButtons);
            meters.Margin = new Thickness(14, 14, 8, 0); w.Layout.Children.Add(meters); w.Footer.Text = preview ? "PREVIEW / SIMULATED AUDIO" : "REAL ENDPOINT PEAK / 20 Hz WHEN VISIBLE";
        }
        else
        {
            w = new("crt", "CRT SETTINGS", 420, 430); var p = new StackPanel { Margin = new Thickness(10, 8, 10, 4) };
            crtControls.Clear();
            crtStateLabel = Retro.Label("", Retro.Green, 11); p.Children.Add(crtStateLabel);
            void Line(string name, Func<double> read, double max, Action<double> change, string hint)
            {
                var g = new Grid { Height = 31 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                g.Children.Add(Retro.Label(name, Retro.Text, 11));
                var valueLabel = Retro.Label("", Retro.Cyan, 11); valueLabel.HorizontalAlignment = HorizontalAlignment.Right; valueLabel.TextWrapping = TextWrapping.NoWrap;
                var slider = new Slider { Minimum = 0, Maximum = 100, Value = read() / max * 100, SmallChange = 1, LargeChange = 10, IsMoveToPointEnabled = true, Height = 25, ToolTip = hint + " / 0-100% / このアプリだけ" };
                AutomationProperties.SetName(slider, name);
                crtControls.Add(name, (slider, valueLabel, read, max));
                slider.ValueChanged += (_, _) =>
                {
                    if (syncingCrtControls) return;
                    change(slider.Value / 100 * max); ApplyAll();
                    crtSaveTimer.Stop(); crtSaveTimer.Start();
                };
                slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => CommitCrtChanges())); slider.LostKeyboardFocus += (_, _) => CommitCrtChanges();
                Grid.SetColumn(slider, 1); Grid.SetColumn(valueLabel, 2); g.Children.Add(slider); g.Children.Add(valueLabel); p.Children.Add(g);
            }
            Line("SCANLINE", () => settings.Scanline, .18, v => settings.Scanline = v, "横方向の走査線を濃くする");
            Line("BLOOM", () => settings.Bloom, .5, v => settings.Bloom = v, "明るい文字と線の光をにじませる");
            Line("RGB EDGE", () => settings.Rgb, .5, v => settings.Rgb = v, "赤と青の輪郭を左右にずらす");
            Line("NOISE", () => settings.Noise, .08, v => settings.Noise = v, "静止した粒状ノイズを重ねる");
            Line("VIGNETTE", () => settings.Vignette, .35, v => settings.Vignette = v, "画面の周辺を暗くする");
            p.Children.Add(Retro.Row(Retro.Button("TEXT -", "文字を小さく（最小11）", () => ChangeText(-1)), Retro.Button("TEXT +", "文字を大きく（最大14）", () => ChangeText(1)), Retro.Button("RAW", "CRT効果をOFF", () => { settings.Crt = false; ApplyAll(); Save(); })));
            p.Children.Add(Retro.Row(Retro.Button("RESTORE", "CRT設定を初期値へ戻す", RestoreCrt), Retro.Button("CRT ON", "CRT効果をON", () => { settings.Crt = true; ApplyAll(); Save(); })));
            p.Children.Add(Retro.Box("LIVE PREVIEW", Retro.Col(
                Retro.Label("TOKU CONTROL", Retro.Green, 18),
                Retro.Label("RGB EDGE / 0123456789", Retro.Cyan, 12),
                Retro.Row(new Border { Width = 80, Height = 5, Background = Retro.Red, Margin = new Thickness(3) }, new Border { Width = 80, Height = 5, Background = Retro.Green, Margin = new Thickness(3) }, new Border { Width = 80, Height = 5, Background = Retro.Cyan, Margin = new Thickness(3) }))));
            w.Layout.Children.Add(p); w.Footer.Text = "STATIC FX / NO FLICKER / NO BEEP";
        }
        // Keep modules independent when the console is minimized, and recoverable
        // through the taskbar / Alt+Tab instead of hiding their taskbar entries.
        modules.Add(key, w); w.ShowInTaskbar = true; w.Apply(settings);
        w.SourceInitialized += (_, _) =>
        {
            if (settings.Windows.TryGetValue(key, out var saved)) WindowPlacement.Place(w, saved);
            else WindowPlacement.OverConsole(this, w);
        };
        w.OnPin = SavePositions;
        w.Closing += (_, _) => settings.Windows[key] = WindowPlacement.Read(w);
        w.Closed += (_, _) =>
        {
            modules.Remove(key);
            if (key == "crt") { crtControls.Clear(); crtStateLabel = null; CommitCrtChanges(); }
            if (key == "meter") audioButtons.RemoveAll(b => Window.GetWindow(b) == w);
            if (!shuttingDown) { Save(); Log("INFO", key + " module closed"); }
        };
        w.LocationChanged += (_, _) => { if (w.IsLoaded) settings.Windows[key] = WindowPlacement.Read(w); };
        w.Show(); if (key == "crt") SyncCrtControls(); if (reveal) RevealModule(w); Tick(); Log("OK", key + " module opened");
    }
    void CommitCrtChanges() { crtSaveTimer.Stop(); Save(); }
    void SyncCrtControls()
    {
        syncingCrtControls = true;
        try
        {
            foreach (var control in crtControls.Values)
            {
                double percent = control.Read() / control.Maximum * 100;
                control.Slider.Value = percent; control.Slider.IsEnabled = settings.Crt;
                control.Value.Text = Math.Round(percent).ToString("0", CultureInfo.InvariantCulture) + "%";
            }
            if (crtStateLabel != null)
            {
                crtStateLabel.Text = settings.Crt ? "CRT ON / LIVE / ALL APP WINDOWS" : "RAW / FX OFF - PRESS CRT ON";
                crtStateLabel.Foreground = settings.Crt ? Retro.Green : Retro.Amber;
            }
        }
        finally { syncingCrtControls = false; }
    }
    void ChangeText(double delta)
    {
        settings.TextSize = Math.Clamp(settings.TextSize + delta, 11, 14);
        ApplyAll(); Save(); Log("OK", "text size: " + settings.TextSize);
    }
    void ApplyAll()
    {
        Apply(settings); foreach (var w in modules.Values) w.Apply(settings);
        // Keep headings and instruments at their calibrated size; scale small labels.
        void Labels(DependencyObject root)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is TextBlock t && t.FontSize >= 11 && t.FontSize <= 14) t.FontSize = settings.TextSize;
                Labels(child);
            }
        }
        Labels(Layout); foreach (var w in modules.Values) Labels(w.Layout);
        SyncCrtControls();
        if (raw != null) raw.Content = settings.Crt ? "RAW" : "CRT ON";
    }
    void RestoreCrt()
    {
        settings.Crt = true; settings.Scanline = .08; settings.Bloom = .22; settings.Noise = .025; settings.Rgb = .25; settings.Vignette = .15;
        ApplyAll(); Save(); Log("OK", "CRT profile restored");
    }
    void Arrange(bool show)
    {
        if (show) { ShowModule("clock", false); ShowModule("meter", false); ShowModule("crt", false); }
        var visible = new[] { "clock", "meter", "crt" }.Where(k => modules.ContainsKey(k) && modules[k].IsVisible).Select(k => modules[k]).ToArray();
        if (!WindowPlacement.Tile(this, visible))
        {
            Footer.Text = "NOT ENOUGH SCREEN SPACE FOR VERTICAL STACK";
            Log("WARN", "not enough work area to tile modules without overlap"); return;
        }
        foreach (var window in visible) window.Activate();
        SavePositions(); Log("OK", "modules stacked beside console / aligned rectangle");
    }

    public async Task ModuleChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        var checks = new Dictionary<string, object>();
        try
        {
            await Task.Delay(1000);
            Button FindButton(string name)
            {
                Button? Find(DependencyObject parent)
                {
                    if (parent is Button b && AutomationProperties.GetName(b) == name) return b;
                    for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                    { var found = Find(VisualTreeHelper.GetChild(parent, i)); if (found != null) return found; }
                    return null;
                }
                return Find(Layout) ?? throw new InvalidOperationException("Missing button " + name);
            }
            void Click(string name) => FindButton(name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            object Bounds(Window w) { var r = WindowPlacement.NativeBounds(w); return new { r.X, r.Y, r.Width, r.Height, w.ActualWidth, w.ActualHeight }; }
            bool Usable(RetroWindow w)
            {
                var r = WindowPlacement.NativeBounds(w); var dpi = VisualTreeHelper.GetDpi(w);
                return w.IsVisible && w.WindowState == WindowState.Normal && r.Width >= w.Width * dpi.DpiScaleX - 2 && r.Height >= w.Height * dpi.DpiScaleY - 2;
            }
            bool BesideConsole(RetroWindow w) => WindowPlacement.WorkArea(this).Contains(WindowPlacement.NativeBounds(w)) && !WindowPlacement.NativeBounds(this).IntersectsWith(WindowPlacement.NativeBounds(w));
            checks["mainBounds"] = Bounds(this);
            checks["startupMeterBounds"] = Bounds(modules["meter"]);
            Click("+ CRT settings"); await Task.Delay(200);
            checks["crtBounds"] = Bounds(modules["crt"]);
            checks["crtButtonCreatesUsableNativeWindow"] = Usable(modules["crt"]);
            checks["crtButtonRevealsBesideConsole"] = BesideConsole(modules["crt"]);
            checks["crtActivated"] = modules["crt"].IsActive;
            Click("SPAWN METER"); await Task.Delay(200);
            checks["meterBounds"] = Bounds(modules["meter"]);
            checks["spawnMeterShowsUsableNativeWindow"] = Usable(modules["meter"]);
            checks["meterButtonRevealsBesideConsole"] = BesideConsole(modules["meter"]);
            checks["meterActivated"] = modules["meter"].IsActive;
            checks["modulesDiscoverableInTaskbar"] = modules.Values.All(w => w.ShowInTaskbar);
            var firstMeter = modules["meter"];
            Click("SPAWN METER"); await Task.Delay(100);
            checks["repeatMeterCallReusesWindow"] = ReferenceEquals(firstMeter, modules["meter"]);
            Click("HIDE ALL"); Click("SPAWN METER"); await Task.Delay(100);
            checks["hiddenMeterRevealed"] = Usable(modules["meter"]) && BesideConsole(modules["meter"]);
            modules["meter"].WindowState = WindowState.Minimized;
            Click("SPAWN METER"); await Task.Delay(100);
            checks["minimizedMeterRestored"] = Usable(modules["meter"]);
            modules["crt"].Close(); Click("+ CRT settings"); await Task.Delay(100);
            checks["closedCrtRecreatedByButton"] = Usable(modules["crt"]) && BesideConsole(modules["crt"]);
            Click("HIDE ALL"); Click("CRT SET"); await Task.Delay(100);
            checks["localToolsCrtButtonReveals"] = Usable(modules["crt"]);
            Click("SPAWN METER");
            var moved = modules["meter"]; var mainRect = WindowPlacement.NativeBounds(this);
            WindowPlacement.Place(moved, new(mainRect.X + mainRect.Width + 12, mainRect.Y, true));
            Click("SPAWN METER"); await Task.Delay(100);
            checks["externalMeterRecalledAndPinPreserved"] = BesideConsole(moved) && moved.Topmost;
            moved.Topmost = false;
            WindowState = WindowState.Minimized; await Task.Delay(100);
            checks["modulesIndependentOfMainMinimize"] = Usable(modules["meter"]) && Usable(modules["crt"]);
            WindowState = WindowState.Normal;
            foreach (var key in new[] { "crt", "meter" }) Capture(modules[key], Path.Combine(directory, key + ".png"));
            checks["passed"] = checks.Where(x => x.Value is bool).All(x => x.Value is true);
            File.WriteAllText(Path.Combine(directory, "module-checks.json"), JsonSerializer.Serialize(checks, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(checks["passed"] is true ? 0 : 1);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString()); Application.Current.Shutdown(1); }
    }

    // Developer verification captures WPF's actual rendered UI. No volume write,
    // drive command, or launcher call is made by this path in native mode.
    public async Task CaptureChecks(string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
            await Task.Delay(2300);
            ShowModule("crt"); ApplyAll(); await Task.Delay(200);
            Capture(this, Path.Combine(directory, "main-crt-on.png"));
            foreach (var w in modules.Values) Capture(w, Path.Combine(directory, w.Key + ".png"));
            settings.Crt = false; ApplyAll(); await Task.Delay(150); Capture(this, Path.Combine(directory, "main-crt-off.png"));
            settings.Crt = true; ApplyAll();
            var test = new Dictionary<string, object> { ["mode"] = preview ? "preview" : "native-read-only", ["audioAvailable"] = state.Available, ["volume"] = state.Volume, ["mute"] = state.Muted, ["opticalCount"] = driveSelect.Items.Count };
            await CheckSystemPanel(directory, test);
            var firstClock = modules["clock"]; ShowModule("clock"); test["clockSingleton"] = ReferenceEquals(firstClock, modules["clock"]);
            var home = WindowPlacement.Read(this);
            WindowPlacement.Place(firstClock, new(home.X + 60, home.Y + 70, false));
            var beforeClose = WindowPlacement.Read(firstClock);
            firstClock.Close(); ShowModule("clock", false); test["clockReopen"] = modules.ContainsKey("clock");
            var afterOpen = WindowPlacement.Read(modules["clock"]);
            test["clockPositionRestored"] = Math.Abs(beforeClose.X - afterOpen.X) < 2 && Math.Abs(beforeClose.Y - afterOpen.Y) < 2;
            modules["meter"].Close(); ShowModule("meter"); test["meterReopen"] = modules.ContainsKey("meter");
            SelectSection("audio"); test["navigationFilters"] = panels["audio"].IsVisible && panels["devices"].Visibility == Visibility.Collapsed; SelectSection("local");
            var meter = modules["meter"]; meter.Hide(); var before = PeakSamples; await Task.Delay(300); test["hiddenMeterStopsSampling"] = before == PeakSamples; meter.Show();
            WindowState = WindowState.Minimized; var systemBefore = SystemSamples; await Task.Delay(1100); test["minimizedStopsSystemSampling"] = systemBefore == SystemSamples; WindowState = WindowState.Normal;
            test["outputListRead"] = outputSelect.Items.Count == outputs.Length;
            test["outputButtonFitsPanel"] = setOutput.TranslatePoint(new Point(0, setOutput.ActualHeight), panels["audio"]).Y <= panels["audio"].ActualHeight - 7;
            if (preview)
            {
                SetVolume(.63f); ToggleMute(); test["previewVolumeMute"] = state.Volume == .63f && state.Muted;
                var original = state;
                outputSelect.SelectedItem = outputs.Single(x => x.Id == "preview-b");
                test["selectionDoesNotSwitch"] = routes.Default(1) == "preview-a";
                await SwitchOutput(); test["previewOutputSwitch"] = state.Device == "preview-b" && state.Volume == .22f && state.Muted;
                outputSelect.SelectedItem = outputs.Single(x => x.Id == "preview-a"); await SwitchOutput();
                test["previewOutputRestoresSavedLevel"] = state == original;
                Capture(this, Path.Combine(directory, "preview-audio-operation.png"));
            }
            foreach (double scale in new[] { 1.25, 1.5 })
            {
                // This is a rendering scale check, not a claim that Windows DPI changed.
                Surface.LayoutTransform = new ScaleTransform(scale, scale); Width = 1080 * scale; Height = 740 * scale; UpdateLayout(); await Task.Delay(100);
                Capture(this, Path.Combine(directory, "layout-scale-" + scale.ToString(CultureInfo.InvariantCulture) + ".png"));
            }
            Surface.LayoutTransform = Transform.Identity; Width = 800; Height = 560; UpdateLayout(); await Task.Delay(100); Capture(this, Path.Combine(directory, "small-window.png")); Width = 1080; Height = 740;
            var proc = Process.GetCurrentProcess(); var start = proc.TotalProcessorTime; var watch = Stopwatch.StartNew();
            await Task.Delay(2500); proc.Refresh(); test["meterVisibleCpuPercentOfMachine"] = (proc.TotalProcessorTime - start).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100; test["workingSetMiB"] = proc.WorkingSet64 / 1048576.0;
            modules["meter"].Hide(); start = proc.TotalProcessorTime; watch.Restart(); await Task.Delay(2500); proc.Refresh(); test["meterHiddenCpuPercentOfMachine"] = (proc.TotalProcessorTime - start).TotalMilliseconds / watch.Elapsed.TotalMilliseconds / Environment.ProcessorCount * 100;
            test["peakSamples"] = PeakSamples; test["systemSamples"] = SystemSamples;
            var required = new[] { "clockSingleton", "clockReopen", "clockPositionRestored", "meterReopen", "navigationFilters", "hiddenMeterStopsSampling", "minimizedStopsSystemSampling" };
            bool passed = required.All(key => test[key] is true) && test["systemPanelPassed"] is true && test["outputListRead"] is true && test["outputButtonFitsPanel"] is true && (!preview || new[] { "previewVolumeMute", "selectionDoesNotSwitch", "previewOutputSwitch", "previewOutputRestoresSavedLevel" }.All(key => test[key] is true));
            test["passed"] = passed;
            File.WriteAllText(Path.Combine(directory, "ui-checks.json"), JsonSerializer.Serialize(test, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(passed ? 0 : 1);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "capture-error.txt"), ex.ToString()); Application.Current.Shutdown(1); }
    }
    static void Capture(Window window, string path)
    {
        window.UpdateLayout(); var visual = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(window); var image = new RenderTargetBitmap(Math.Max(1, (int)(visual.ActualWidth * dpi.DpiScaleX)), Math.Max(1, (int)(visual.ActualHeight * dpi.DpiScaleY)), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        image.Render(visual); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(path); encoder.Save(file);
    }
}

static class Checks
{
    public static int Run(string[] args)
    {
        var i = Array.IndexOf(args, "--result"); var path = i >= 0 && i + 1 < args.Length ? args[i + 1] : Path.Combine(Path.GetTempPath(), "toku-control-checks.json");
        var results = new Dictionary<string, object>(); bool passed = true;
        void Check(string name, bool ok) { results[name] = ok; passed &= ok; }
        string temp = Path.Combine(Path.GetTempPath(), "toku-control-test-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var store = new SettingsStore(temp); File.WriteAllText(store.FilePath, "{broken-json"); var value = store.Load();
            Check("corruptSettingsDefaults", value.Crt && store.Warning != null); Check("corruptSettingsPreserved", Directory.GetFiles(temp, "*.invalid-*").Length == 1);
            value.Scanline = 100; value.Windows["clock"] = new(-9999, -8888, true, 420, 212); Check("atomicSettingsSave", store.Save(value) == null);
            var reload = store.Load(); Check("settingsClamp", reload.Scanline == .18); Check("windowPositionRoundTrip", reload.Windows["clock"].Pin && reload.Windows["clock"].X == -9999);
            Check("windowSizeRoundTrip", reload.Windows["clock"].Width == 420 && reload.Windows["clock"].Height == 212);
            File.WriteAllText(store.FilePath, "{\"Windows\":{\"main\":{\"X\":20,\"Y\":30,\"Pin\":false}}}");
            var legacy = new SettingsStore(temp).Load(); Check("legacyWindowSettingsCompatible", legacy.Windows["main"].X == 20 && legacy.Windows["main"].Width == null && legacy.Windows["main"].Height == null);
            legacy.Windows["main"] = new(20, 30, false, double.NaN, -10); store.Save(legacy);
            Check("invalidWindowSizesIgnored", store.Load().Windows["main"].Width == null && store.Load().Windows["main"].Height == null);
            var ring = new RingLog(); for (int n = 0; n < 1500; n++) ring.Add("TEST", n.ToString()); Check("logBound1000", ring.Lines.Count == 1000); Check("logPreservesNewest", ring.Lines.Last().EndsWith("1499"));
            ring.Clear(); Check("logClear", ring.Lines.Count == 0);
            using var simulated = new PreviewAudio(); simulated.SetVolume(.37f); simulated.ToggleMute(); Check("mutePreservesLevel", simulated.Read().Volume == .37f && simulated.Read().Muted); Check("volumeClamp", simulated.SetVolume(5).Volume == 1);
            using var native = new CoreAudio(); var a = native.Read(); results["nativeRead"] = a; Check("nativeReadNoThrow", true);
            foreach (var test in RoutingChecks.Run().GetAwaiter().GetResult()) Check(test.Key, test.Value);
            using var routes = new WindowsOutputRoutes(); results["nativeOutputs"] = routes.List();
            results["nativeDefaults"] = Enumerable.Range(0, 3).Select(routes.Default).ToArray();
            var probe = new SystemProbe(); probe.Read(); Thread.Sleep(1200); var p = probe.Read(); results["systemRead"] = p; Check("systemValuesValid", p.Cpu is >= 0 and <= 100 && p.Ram is >= 0 and <= 100 && p.Disk is >= 0 and <= 100);
            Check("displayInteropSizes", SystemInformation.InteropSizesValid);
            Check("fractionalRefreshAndUnavailableFormatting", SystemInformation.RefreshRate(60000d / 1001) == "59.94 Hz" && SystemInformation.RefreshRate(null) == "N/A" && SystemInformation.Capacity(null) == "N/A");
            var machine = SystemInformation.Read(); results["machineSnapshot"] = machine;
            Check("machineRead", machine.LogicalProcessors > 0 && machine.UsableBytes > 0 && machine.Cpu != "UNAVAILABLE" && machine.Windows.Length > 0);
            Check("activeDisplayRead", machine.Monitors.Length > 0 && machine.Monitors.Any(m => m.Primary) && machine.Monitors.All(m => m.Width > 0 && m.Height > 0));
            results["opticalDevices"] = OpticalDrives.Scan();
            results["osWritesPerformed"] = false;
        }
        catch (Exception ex) { passed = false; results["exception"] = ex.ToString(); }
        results["passed"] = passed; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })); return passed ? 0 : 1;
    }
}
