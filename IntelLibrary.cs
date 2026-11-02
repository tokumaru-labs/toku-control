using System.IO;
using System.Text.Json;

namespace TokuControl;

internal sealed record IntelSnapshot(DateTimeOffset Fetched, IntelArticle[] Articles);
internal sealed record IntelArchive(Dictionary<IntelSource, IntelSnapshot> Feeds, IntelArticle[] Saved);
internal sealed class IntelLibrary(string directory)
{
    const int MaxBytes = 2_000_000;
    string FileName => Path.Combine(directory, "intel-library-v1.json");
    public readonly Dictionary<IntelSource, IntelSnapshot> Feeds = [];
    public readonly List<IntelArticle> Saved = [];
    public string? Warning { get; private set; }
    internal static bool Valid(IntelArticle? a) => a != null && Enum.IsDefined(a.Source) && !string.IsNullOrWhiteSpace(a.Title) && a.Title.Length <= 260 &&
        IntelFeeds.WebLink(a.Url) != null && (a.Discussion == null || IntelFeeds.WebLink(a.Discussion) is { Host: "news.ycombinator.com", AbsolutePath: "/item" });
    public void Load()
    {
        try
        {
            if (!File.Exists(FileName)) return;
            using var file = File.OpenRead(FileName);
            if (file.Length > MaxBytes) throw new InvalidDataException();
            var archive = JsonSerializer.Deserialize<IntelArchive>(file) ?? throw new JsonException();
            if (archive.Feeds == null || archive.Saved == null) throw new JsonException();
            foreach (var (source, snap) in archive.Feeds.Where(kv => Enum.IsDefined(kv.Key)))
            {
                if (snap?.Articles == null) continue;
                Feeds[source] = new(snap.Fetched, snap.Articles.Where(a => Valid(a) && a.Source == source).DistinctBy(a => a.Key).Take(32).ToArray());
            }
            Saved.AddRange(archive.Saved.Where(Valid).DistinctBy(a => a.Key).Take(64));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException)
        { Warning = "保存データを読めません / " + ex.GetType().Name; }
    }
    public void Store(IntelSource source, IntelBatch batch)
    {
        Feeds[source] = new(DateTimeOffset.UtcNow, batch.Items.Where(a => Valid(a) && a.Source == source).DistinctBy(a => a.Key).Take(32).ToArray()); Save();
    }
    public void ToggleSaved(IntelArticle a)
    {
        if (!Valid(a)) return;
        if (Saved.RemoveAll(b => b.Key == a.Key) == 0)
        {
            if (Saved.Count >= 64) { Warning = "あとで読むは64件までです。既存の保存を解除してください。"; return; }
            Saved.Insert(0, a);
        }
        Save();
    }
    void Save()
    {
        var temp = FileName + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(directory);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(new IntelArchive(Feeds, [.. Saved]));
            if (bytes.Length > MaxBytes) throw new InvalidDataException("cache limit");
            File.WriteAllBytes(temp, bytes); File.Move(temp, FileName, true); Warning = null;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException)
        { Warning = "保存できません（今の画面では読めます） / " + ex.GetType().Name; }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
