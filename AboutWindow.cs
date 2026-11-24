using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace TokuControl;

internal sealed class AboutWindow : Window
{
    static readonly Uri OfficialSite = new("https://tokumaru-labs.github.io/");

    internal static void Open(Window owner, bool preview)
    {
        var existing = owner.OwnedWindows.OfType<AboutWindow>().FirstOrDefault();
        if (existing != null)
        {
            if (existing.WindowState == WindowState.Minimized) existing.WindowState = WindowState.Normal;
            existing.Activate();
            return;
        }
        new AboutWindow(preview) { Owner = owner }.Show();
    }

    AboutWindow(bool preview)
    {
        Title = "このアプリについて";
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Retro.Background;
        FontFamily = new FontFamily("Meiryo UI, Segoe UI");

        var developer = Retro.Label("開発元　Tokumaru Labs", Retro.Text, 16);
        var website = Retro.Label("公式サイト　", Retro.Text, 14);
        var link = new Hyperlink(new Run(OfficialSite.AbsoluteUri))
        {
            NavigateUri = OfficialSite, Foreground = Retro.Cyan, IsEnabled = !preview,
            ToolTip = preview ? "プレビュー中はブラウザーを開きません" : "既定のブラウザーで公式サイトを開く"
        };
        AutomationProperties.SetName(link, "Tokumaru Labs 公式サイト");
        link.RequestNavigate += (_, e) =>
        {
            e.Handled = true;
            if (preview) return;
            try { using var browser = Process.Start(new ProcessStartInfo(OfficialSite.AbsoluteUri) { UseShellExecute = true }); }
            catch (Win32Exception)
            {
                MessageBox.Show(this, "ブラウザーを開けませんでした。\n" + OfficialSite.AbsoluteUri,
                    "Tokumaru Labs 公式サイト", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        };
        website.Inlines.Add(link);
        var close = Retro.Button("閉じる", "元の画面に戻る（Esc）", Close);
        close.HorizontalAlignment = HorizontalAlignment.Right;
        close.MinWidth = 88;
        close.Margin = new Thickness(3, 16, 3, 3);
        Content = new Border { Padding = new Thickness(18), Child = Retro.Col(
            Retro.Label("TOKU CONTROL  v" + GithubUpdates.AppVersion, Retro.Green, 20), developer, website,
            Retro.Label("本体ライセンス　GPL-3.0-only", Retro.Text, 13), close) };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }
}
