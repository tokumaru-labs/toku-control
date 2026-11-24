using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace TokuControl;

// A view of the manual Radar session. Opening this panel or its evidence never starts a probe.
internal sealed class RadarSnapshotPanel : StackPanel
{
    static readonly (string Label, RadarSource[] Sources)[] groups = [
        ("保護", [RadarSource.Defender, RadarSource.Firewall]),
        ("自動起動", RadarKnowledge.Persistence),
        ("認証", [RadarSource.Authentication]),
        ("通信", [RadarSource.Tcp, RadarSource.Udp])
    ];
    readonly bool demo;
    readonly TextBlock stamp = Retro.Label("", Retro.Cyan, 10);
    readonly Button[] buttons = new Button[groups.Length];
    readonly RadarSource[] targets = new RadarSource[groups.Length];
    RadarSession? session;
    internal IReadOnlyList<Button> Buttons => buttons;

    public RadarSnapshotPanel(bool demo, Action<RadarSource> open)
    {
        this.demo = demo;
        stamp.Margin = new Thickness(1, 0, 1, 0); Children.Add(stamp);
        for (int i = 0; i < groups.Length; i++)
        {
            int index = i;
            var b = Retro.Button("", "", () => open(targets[index]));
            b.FontSize = 10; b.MinHeight = 18; b.Padding = new Thickness(3, 0, 3, 0);
            b.Margin = new Thickness(0, 1, 0, 0); b.HorizontalContentAlignment = HorizontalAlignment.Left;
            buttons[i] = b; Children.Add(b);
        }
        Render();
    }

    internal void Bind(RadarSession? next)
    {
        if (ReferenceEquals(session, next)) return;
        if (session != null) session.Changed -= Render;
        session = next;
        if (session != null) session.Changed += Render;
        Render();
    }

    void Render()
    {
        var latest = session?.Current.Values.MaxBy(r => r.CapturedAt);
        stamp.Text = (demo ? "DEMO" : "RADAR") + " · " +
            (session?.Busy == true ? "取得中" : latest == null ? "未取得" : latest.CapturedAt.ToLocalTime().ToString("MM/dd HH:mm"));
        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var states = group.Sources.Select(source => {
                var read = session?.Current.GetValueOrDefault(source);
                var assessment = read == null ? new RadarAssessment(RadarSignal.Unknown, "未取得", "") :
                    RadarAnalysis.Assess(read, session!.Reference.GetValueOrDefault(source));
                return (source, read, assessment);
            }).ToArray();
            // Keep both facts visible when a known warning coexists with missing evidence.
            var first = states.OrderBy(s => s.assessment.Signal switch {
                RadarSignal.Review => 0, RadarSignal.Unknown => 1, RadarSignal.NotCompared => 2, _ => 3
            }).First();
            targets[i] = first.source;
            bool unknown = states.Any(s => s.assessment.Signal == RadarSignal.Unknown);
            string state = first.assessment.Signal switch {
                RadarSignal.Review => unknown ? "要確認+未確認" : "要確認",
                RadarSignal.NotCompared => "未比較",
                RadarSignal.Unknown => states.All(s => s.read == null) ? "未取得" : "未確認",
                _ => "観測済"
            };
            string caption = group.Label + "  " + state;
            buttons[i].Content = caption;
            buttons[i].Foreground = first.assessment.Signal == RadarSignal.Review ? Retro.Amber :
                first.assessment.Signal == RadarSignal.Observed ? Retro.Cyan : Retro.Text;
            AutomationProperties.SetName(buttons[i], caption + "、根拠を開く");
            buttons[i].ToolTip = string.Join(Environment.NewLine, states.Select(s =>
                RadarKnowledge.Title(s.source) + " · " + s.assessment.Summary +
                (s.read == null ? "" : " · " + s.read.CapturedAt.ToLocalTime().ToString("MM/dd HH:mm"))));
        }
    }
}
