using System.IO;
using System.Text;

namespace TokuControl;

internal static class AutorunscChecks
{
    internal const string Header = "Entry Location,Entry,Category,Profile,Enabled,Image Path,Launch String\r\n";
    internal const string First = "Run,Fixture,Logon,User,enabled,C:\\sample.exe,\"cmd, \"\"日本語\"\"\"\r\n";
    internal static async Task<Dictionary<string, bool>> Run()
    {
        var checks = new Dictionary<string, bool>();
        static List<StartupItem> Parse(string csv) => AutorunscSnapshot.Parse(Encoding.UTF8.GetBytes(csv), default);
        var initial = Parse(Header + First);
        var utf16 = AutorunscSnapshot.Parse(Encoding.Unicode.GetBytes(Header + First), default);
        checks["autorunsc CSV quotes and UTF-16 without BOM"] = initial.SequenceEqual(utf16) &&
            initial.Single().Value.Command == "cmd, \"日本語\"";
        var multiline = Parse(Header + First.Replace("日本語", "one\r\ntwo"));
        checks["autorunsc multiline quoted field"] = multiline.Single().Value.Command.Contains("one\r\ntwo");
        string second = First.Replace("sample.exe", "updated.exe");
        var changed = AutorunscSnapshot.Compare(initial, Parse(Header + second));
        checks["autorunsc changed startup command preserves both values"] = changed.Count == 1 &&
            changed[0].Kind == "変更" && changed[0].Before?.Image == @"C:\sample.exe" && changed[0].After?.Image == @"C:\updated.exe";
        checks["autorunsc duplicate records compare as a multiset"] =
            AutorunscSnapshot.Compare(Parse(Header+First+second), Parse(Header+second+First)).Count == 0 &&
            AutorunscSnapshot.Compare(Parse(Header+First+First), initial).Single().Kind == "削除" &&
            AutorunscSnapshot.Compare(initial, Parse(Header+First+First)).Single().Kind == "追加";
        checks["autorunsc profile is part of the identity"] =
            AutorunscSnapshot.Compare(initial, Parse(Header+First.Replace("User", "Other"))).Count == 2;
        checks["autorunsc empty inventory retains header schema"] = Parse(Header).Count == 0;
        bool Rejected(string csv)
        {
            try { Parse(csv); return false; }
            catch (Exception e) when (e is InvalidDataException or Microsoft.VisualBasic.FileIO.MalformedLineException) { return true; }
        }
        checks["autorunsc malformed and truncated CSV rejected"] = Rejected("") && Rejected(Header.Replace("Profile", "Entry")) &&
            Rejected(Header+"too,few,columns") && Rejected(Header+"\"unterminated");
        var info = AutorunscReader.StartInfo(@"C:\verified\autorunsc64.exe");
        checks["autorunsc fixed read-only arguments no shell or elevation"] = !info.UseShellExecute && info.Verb == "" &&
            info.ArgumentList.SequenceEqual(new[] { "-nobanner", "-a", "*", "-c" });
        bool Refused(string path)
        {
            try { AutorunscReader.LocalFile(path); return false; }
            catch (InvalidDataException) { return true; }
        }
        checks["autorunsc rejects UNC device relative and alternate stream paths"] =
            Refused(@"\\server\share\fixture.csv") && Refused(@"\\?\C:\file") && Refused("relative.csv") && Refused(@"C:\file:stream");
        using var stream = new MemoryStream(new byte[20]);
        try { await AutorunscReader.ReadBounded(stream, 16, default); checks["autorunsc bounded read"] = false; }
        catch (InvalidDataException) { checks["autorunsc bounded read"] = true; }
        try { await AutorunscReader.Read("unused", false, new CancellationToken(true)); checks["autorunsc cancelled before access"] = false; }
        catch (OperationCanceledException) { checks["autorunsc cancelled before access"] = true; }
        var dir = Directory.CreateTempSubdirectory();
        var fake = Path.Combine(dir.FullName, "autorunsc64.exe");
        try
        {
            await File.WriteAllTextAsync(fake, "fixture, not executable");
            try { await AutorunscReader.Read(fake, true, default); checks["autorunsc unknown image blocked"] = false; }
            catch (Exception e) when (e is InvalidDataException or InvalidOperationException) { checks["autorunsc unknown image blocked"] = true; }
        }
        finally { File.Delete(fake); dir.Delete(); }
        return checks;
    }
}
