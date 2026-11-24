using System.IO;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace TokuControl;

internal sealed record StartupIdentity(string Profile, string Category, string Location, string Entry);
internal sealed record StartupValue(string Enabled, string Image, string Command);
internal sealed record StartupItem(StartupIdentity Identity, StartupValue Value);
internal sealed record StartupChange(string Kind, StartupIdentity Identity, StartupValue? Before, StartupValue? After);

internal static class AutorunscSnapshot
{
    internal const int MaxBytes = 8 * 1024 * 1024;
    internal const int MaxRows = 12000;

    internal static List<StartupItem> Parse(byte[] bytes, CancellationToken token)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("CSV が 8 MiB を超えています。");
        // Autorunsc and shell redirection can both produce UTF-16, including without a BOM.
        Encoding encoding = bytes.Length > 1 && bytes[1] == 0 ? new UnicodeEncoding(false, false, true)
            : new UTF8Encoding(false, true);
        using var stream = new MemoryStream(bytes, false);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        using var parser = new TextFieldParser(reader) { TextFieldType = FieldType.Delimited, TrimWhiteSpace = false };
        parser.SetDelimiters(",");
        var columns = parser.ReadFields() ?? throw new InvalidDataException("CSV の見出しがありません。");
        int Index(string name)
        {
            var matches = columns.Select((column, index) => (column, index))
                .Where(c => c.column.Trim().Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
            return matches.Length == 1 ? matches[0].index : throw new InvalidDataException($"CSV の列を確認してください ({name})。");
        }
        int location = Index("Entry Location"), entry = Index("Entry"), profile = Index("Profile"), category = Index("Category");
        int enabled = Index("Enabled"), image = Index("Image Path"), command = Index("Launch String");
        var rows = new List<StartupItem>();
        while (!parser.EndOfData)
        {
            token.ThrowIfCancellationRequested();
            var fields = parser.ReadFields();
            if (fields == null || fields.Length != columns.Length || fields.Any(s => s.Contains('\0')))
                throw new InvalidDataException("CSV に読み取れない行があります。");
            if (rows.Count >= MaxRows) throw new InvalidDataException("CSV が 12,000 行を超えています。");
            rows.Add(new(new(fields[profile], fields[category], fields[location], fields[entry]),
                new(fields[enabled], fields[image], fields[command])));
        }
        return rows;
    }

    // Match repeated entries as multisets. A change with an ambiguous pairing is shown as removals/additions.
    internal static List<StartupChange> Compare(IReadOnlyList<StartupItem> baseline, IReadOnlyList<StartupItem> current)
    {
        var before = baseline.ToLookup(r => r.Identity, r => r.Value);
        var after = current.ToLookup(r => r.Identity, r => r.Value);
        var changes = new List<StartupChange>();
        foreach (var key in before.Select(g => g.Key).Union(after.Select(g => g.Key)))
        {
            var counts = new Dictionary<StartupValue, int>();
            foreach (var value in before[key]) counts[value] = counts.GetValueOrDefault(value) - 1;
            foreach (var value in after[key]) counts[value] = counts.GetValueOrDefault(value) + 1;
            var removed = counts.Where(p => p.Value < 0).SelectMany(p => Enumerable.Repeat(p.Key, -p.Value)).ToArray();
            var added = counts.Where(p => p.Value > 0).SelectMany(p => Enumerable.Repeat(p.Key, p.Value)).ToArray();
            if (removed.Length == 1 && added.Length == 1)
                changes.Add(new("変更", key, removed[0], added[0]));
            else
            {
                changes.AddRange(removed.Select(v => new StartupChange("削除", key, v, null)));
                changes.AddRange(added.Select(v => new StartupChange("追加", key, null, v)));
            }
        }
        return changes;
    }
}
