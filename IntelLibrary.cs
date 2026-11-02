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
    static bool StorageError(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or NotSupportedException;
    IntelArchive ReadLatest()
    {
        if (!File.Exists(FileName)) return new([], []);
        using var file = new FileStream(FileName, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (file.Length > MaxBytes) throw new InvalidDataException("cache limit");
        var data = JsonSerializer.Deserialize<IntelArchive>(file) ?? throw new JsonException();
        if (data.Feeds == null || data.Saved == null) throw new JsonException();
        var feeds = new Dictionary<IntelSource, IntelSnapshot>();
        foreach (var (source, snap) in data.Feeds.Where(kv => Enum.IsDefined(kv.Key)))
        {
            if (snap?.Articles == null) continue;
            feeds[source] = new(snap.Fetched, snap.Articles.Where(a => Valid(a) && a.Source == source).DistinctBy(a => a.Key).Take(32).ToArray());
        }
        return new(feeds, data.Saved.Where(Valid).DistinctBy(a => a.Key).Take(64).ToArray());
    }
    void Adopt(IntelArchive data)
    {
        Feeds.Clear(); foreach (var (s, snapshot) in data.Feeds) Feeds[s] = snapshot;
        Saved.Clear(); Saved.AddRange(data.Saved);
    }
    public void Load()
    {
        try { Adopt(ReadLatest()); Warning = null; }
        catch (Exception ex) when (StorageError(ex))
        { Warning = "保存データを読めません / " + ex.GetType().Name; }
    }
    public void Store(IntelSource source, IntelBatch batch)
    {
        var snap = new IntelSnapshot(DateTimeOffset.UtcNow, batch.Items.Where(a => Valid(a) && a.Source == source).DistinctBy(a => a.Key).Take(32).ToArray());
        Feeds[source] = snap; // Fresh news remains readable in this window even if the disk is unavailable.
        Update(data => { data.Feeds[source] = snap; return data; });
    }
    public void ToggleSaved(IntelArticle a)
    {
        if (!Valid(a)) return;
        bool remove = Saved.Any(b => b.Key == a.Key); // Apply the visible action, even if a second window already did the same thing.
        Update(data =>
        {
            var others = data.Saved.Where(b => b.Key != a.Key).ToArray();
            if (!remove && others.Length >= 64) { Warning = "あとで読むは64件までです。既存の保存を解除してください。"; return null; }
            return data with { Saved = remove ? others : [a, .. others] };
        });
    }
    void Update(Func<IntelArchive, IntelArchive?> change)
    {
        var temp = FileName + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(directory);
            using var writer = ProfileWriteLock.Open(FileName);
            var latest = ReadLatest(); var updated = change(latest);
            if (updated == null) { Adopt(latest); return; }
            var bytes = JsonSerializer.SerializeToUtf8Bytes(updated);
            if (bytes.Length > MaxBytes) throw new InvalidDataException("cache limit");
            File.WriteAllBytes(temp, bytes); File.Move(temp, FileName, true); Adopt(updated); Warning = null;
        }
        catch (Exception ex) when (StorageError(ex))
        { Warning = "ディスクに保存できません / " + ex.GetType().Name; }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
