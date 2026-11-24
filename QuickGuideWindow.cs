using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace TokuControl;

internal sealed class QuickGuideWindow : Window
{
    internal enum Topic { Radar, Changes, Intel, Clock }
    readonly TabControl topics = new() { Background = Retro.Panel, BorderBrush = Retro.Border, Padding = new Thickness(12) };

    internal static Button Entry(Window owner, Topic topic)
    {
        return Retro.Button("使い方", "この画面の使い方をオフラインで読む", () =>
        {
            var guide = owner.OwnedWindows.OfType<QuickGuideWindow>().FirstOrDefault();
            if (guide == null)
            {
                guide = new QuickGuideWindow { Owner = owner };
                guide.topics.SelectedIndex = (int)topic;
                guide.Show();
            }
            else
            {
                guide.topics.SelectedIndex = (int)topic;
                if (guide.WindowState == WindowState.Minimized) guide.WindowState = WindowState.Normal;
                guide.Activate();
            }
        });
    }

    QuickGuideWindow()
    {
        Title = "使い方 · TOKU CONTROL";
        Width = 620; Height = 510; MinWidth = 350; MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Retro.Background;
        FontFamily = new System.Windows.Media.FontFamily("Meiryo UI, Segoe UI");
        AutomationProperties.SetName(topics, "使い方の項目");
        AddPage("PCを点検", "このPCの保護と通信を確認する",
            ("01  まず1回、状態を読む", "メインの「+ security radar」で READ / REFRESH を押します。SOURCES → Windows Defender または Windows Firewall を選び、取得時刻を見ます。保護がオフなら Windows セキュリティや利用中の対策製品でも状態を確認します。"),
            ("02  気になる項目の根拠を見る", "ATTACK MAP は手口と確認項目の対応図です。項目を押して「次の確認」へ。OBSERVED は取得した範囲、REVIEW は照合が必要な項目、UNKNOWN は権限不足などで確認できなかった範囲を示します。"),
            ("03  見覚えのない通信やログオンがあったら", "TCP connections ではアプリ名と PID を見ます。自分が開いたブラウザーを通常終了して再取得すると、接続の変化を比べられます。Authentication / 24h のログオン失敗は、まず時刻とアカウントを自分の操作と照合します。"),
            ("04  Defender の検出があったら", "「検出履歴」で直近24時間の検出名と時刻を読みます。前後5分に取得した TCP・UDP 一覧の同名候補は、通信元を確定する情報ではありません。Windows セキュリティの「保護の履歴」で対処結果を確認します。"));
        AddPage("前後を比較", "アプリを入れる前後で自動起動の変化を見る",
            ("01  インストール前に基準を作る", "Security Radar で READ / REFRESH。自動起動・サービス・タスクに関する4項目をすべて取得できたら、一覧を確認して SET REFERENCE を押します。"),
            ("02  アプリを入れた後でもう一度読む", "TOKU CONTROL を起動したまま再取得し、SOURCES から Run / RunOnce、Startup folders、Services、Scheduled tasks を選びます。基準はアプリ終了時に消えます。"),
            ("03  追加・変更の理由を確かめる", "BEFORE と AFTER の名前やパスを、直前に入れたアプリと照合します。心当たりがなければ、そのファイルを開かずに Windows セキュリティの「保護の履歴」を確認します。"));
        AddPage("ニュース", "選んだ情報源からニュースを読む",
            ("01  読みたい情報源を選ぶ", "メインの「+ intel stream」を開き、読みたい情報源にチェックを入れます。「オンライン取得」をオンにすると、選んだ配信から見出しが届きます。"),
            ("02  見出しを止めて読む", "「Ⅱ 一時停止」で流れを止め、気になる見出しを選びます。「原文を開く」で記事をブラウザーに表示できます。"),
            ("03  オフラインで振り返る", "通信を止めるには「オンライン取得」をオフにします。取得済みの見出しは「CACHE / 履歴」、保存した見出しは「★ あとで読む」で確認できます。"));
        AddPage("時計・小窓", "集中する時間と休憩を選ぶ",
            ("01  一度だけ休憩する", "「clock module」で時計の表示を切り替えます。時計のホイールで分数を選んで START。途中でやめるときは RESET を押します。"),
            ("02  集中と休憩を繰り返す", "「20/5 ▶」で集中20分と休憩5分が交互に始まります。左上のテーマで「宵環曼荼羅」を選ぶと、休憩を完了した日ごとに絵が広がります。"),
            ("03  時計から根拠を見る", "テーマを「TRACE 端末」に切り替え、保護・自動起動・認証・通信の項目を押すと、Security Radar で最後に読み取った結果を開けます。"));

        var layout = new DockPanel();
        var heading = Retro.Label("TOKU CONTROL   /   使い方", Retro.Cyan, 18);
        DockPanel.SetDock(heading, Dock.Top); layout.Children.Add(heading);
        var close = Retro.Button("閉じる", "元の画面に戻る（Esc）", Close);
        close.HorizontalAlignment = HorizontalAlignment.Right; close.MinWidth = 90;
        DockPanel.SetDock(close, Dock.Bottom); layout.Children.Add(close);
        topics.Margin = new Thickness(0, 8, 0, 8); layout.Children.Add(topics);
        Content = new Border { Background = Retro.Background, Padding = new Thickness(14), Child = layout };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    void AddPage(string tab, string title, params (string Heading, string Text)[] steps)
    {
        var body = Retro.Col(Retro.Label(title, Retro.Green, 17));
        foreach (var step in steps)
        {
            var label = Retro.Label(step.Heading, Retro.Cyan, 14);
            label.Margin = new Thickness(3, 12, 3, 2);
            body.Children.Add(label);
            body.Children.Add(Retro.Label(step.Text, Retro.Text, 14));
        }
        topics.Items.Add(new TabItem { Header = tab, Padding = new Thickness(10, 5, 10, 5),
            Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } });
    }
}
