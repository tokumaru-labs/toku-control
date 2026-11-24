using System.Globalization;
using System.Text;

namespace TokuControl;

internal static class DefenderHistoryEvidence
{
    static string Time(DateTimeOffset? time) => time?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "不明";
    static bool Control(char c) => char.IsControl(c) ||
        char.GetUnicodeCategory(c) is UnicodeCategory.Format or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;

    static string Field(string value) => string.IsNullOrWhiteSpace(value) ? "不明" :
        string.Concat(value.Select(c => Control(c) ? '�' : c));

    // Defender supplies a path or process name. Compare only an unambiguous executable filename.
    // In particular, never expand, open or resolve paths from event data.
    static string? ExecutableName(string value)
    {
        if (value.Any(Control)) return null;
        var path = value.Trim();
        if (path.Length >= 2 && path[0] == '"' && path[^1] == '"') path = path[1..^1];
        if (path.Contains(".exe ", StringComparison.OrdinalIgnoreCase) || path.Contains(" -", StringComparison.Ordinal) ||
            path.IndexOfAny(['/', '"', '<', '>', '|', '*', '?', '&', ';', '`']) >= 0) return null;
        bool drive = path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] == '\\';
        bool unc = path.StartsWith(@"\\", StringComparison.Ordinal);
        var parts = (drive ? path[3..] : unc ? path[2..] : path).Split('\\');
        if ((unc && parts.Length < 3) || (!drive && !unc && parts.Length != 1)) return null;
        // Reject relative segments, drive/stream syntax, device aliases and ambiguous components.
        if (parts.Any(part => part.Length == 0 || part.Contains(':') || part.EndsWith('.') || part.EndsWith(' '))) return null;
        var file = parts[^1];
        if (!file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        var name = file[..^4];
        return string.IsNullOrWhiteSpace(name) ||
            name.ToUpperInvariant() is "UNKNOWN" or "UNKNOWN PROCESS" or "UNAVAILABLE" or "NONE" or "NULL" or "-" ? null : name;
    }

    public static string Describe(RadarRow row, RadarRead? tcp, RadarRead? udp)
    {
        var text = new StringBuilder().AppendLine("検出名: " + Field(row.Label))
            .AppendLine("記録時刻: " + Time(row.RecordedAt)).AppendLine("検出時刻: " + Time(row.DetectedAt))
            .AppendLine("記録上のプロセス: " + Field(row.Value));
        var name = ExecutableName(row.Value);
        Match("TCP", tcp);
        Match("UDP", udp);
        return text.ToString();

        void Match(string protocol, RadarRead? snapshot)
        {
            if (snapshot == null) { text.AppendLine(protocol + " 未取得 / 照合保留"); return; }
            text.Append($"{protocol} 取得 {Time(snapshot.CapturedAt)} / ");
            if (snapshot.Coverage != RadarCoverage.Complete)
            { text.AppendLine(RadarKnowledge.Coverage(snapshot.Coverage) + " / 照合保留"); return; }
            if (row.DetectedAt == null) { text.AppendLine("検出時刻不明 / 照合保留"); return; }
            var delta = snapshot.CapturedAt - row.DetectedAt.Value;
            text.Append($"検出の{(delta < TimeSpan.Zero ? "前" : "後")} {delta.Duration().TotalSeconds:0.###} 秒 / ");
            if (delta.Duration() > TimeSpan.FromMinutes(5)) { text.AppendLine("5分を超えるため照合対象外"); return; }
            if (name == null) { text.AppendLine("照合可能なプロセス名なし"); return; }
            int unresolved = snapshot.Rows.Count(r => r.Owner is not { Pid: > 0 } || string.IsNullOrWhiteSpace(r.Owner.Name));
            if (unresolved > 0) text.Append($"名前・PID不明 {unresolved} 行 / ");
            var matches = snapshot.Rows.Where(r => r.Owner is { Pid: > 0 }).GroupBy(r => r.Owner!.Pid)
                .Where(g => g.All(r => string.Equals(r.Owner!.Name, name, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(g => g.Key).ToArray();
            if (matches.Length == 0) { text.AppendLine("取得できた名前・PIDの範囲で同名候補なし"); return; }
            text.Append("同名候補 ").Append(matches.Length).Append(" PID / ")
                .AppendJoin("、", matches.Take(10).Select(g => $"PID {g.Key} ({g.Count()} 行)"));
            if (matches.Length > 10) text.Append("（先頭10 PIDを表示）");
            text.AppendLine();
        }
    }
}
