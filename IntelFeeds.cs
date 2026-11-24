using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace TokuControl;

internal enum IntelSource { HackerNews, JpCert, HuggingFace }
internal sealed record IntelArticle(IntelSource Source, string Title, string Url, DateTimeOffset? Date, string? Discussion = null)
{
    public string Key => Source + "|" + (Discussion ?? Url);
    public string Topic => IntelFeeds.Topic(Title);
}
internal sealed record IntelBatch(IntelArticle[] Items, string? Warning = null);
internal interface IIntelReader { Task<IntelBatch> ReadAsync(IntelSource source, CancellationToken token); }

// Only fixed distribution endpoints are requested. Article URLs are inert metadata until a user opens one.
internal sealed class IntelFeeds : IIntelReader
{
    internal static readonly HttpClient Client = new(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli
    });
    readonly HttpClient client;
    public IntelFeeds(HttpClient? client = null) => this.client = client ?? Client;
    const string HnApi = "https://hacker-news.firebaseio.com/v0/";
    internal const string JpUrl = "https://www.jpcert.or.jp/rss/jpcert.rdf", HfUrl = "https://huggingface.co/blog/feed.xml";
    public static string Name(IntelSource source) => source switch { IntelSource.HackerNews => "Hacker News", IntelSource.JpCert => "JPCERT/CC", IntelSource.HuggingFace => "Hugging Face Blog", _ => throw new ArgumentOutOfRangeException(nameof(source)) };
    public static string Short(IntelSource source) => source switch { IntelSource.HackerNews => "HN", IntelSource.JpCert => "SEC", _ => "AI" };
    public static string DateKind(IntelSource source) => source switch { IntelSource.HackerNews => "HN投稿", IntelSource.JpCert => "配信・更新", _ => "公開" };
    public static string Topic(string title)
    {
        bool Has(params string[] words) => words.Any(s => title.Contains(s, StringComparison.OrdinalIgnoreCase));
        if (Has("Windows", ".NET", "Microsoft", "マイクロソフト")) return "Windows / .NET";
        if (Has("security", "vulnerab", "CVE-", "exploit", "sandbox", "脆弱性", "不正アクセス")) return "Security";
        if (Has("LLM", "inference", "AI ", " AI", "model", "agent", "GPU", "instruct", "Claude", "transformer")) return "AI / 開発";
        return "技術・話題";
    }
    public static string Text(string? value) => new((WebUtility.HtmlDecode(value ?? "").Trim().Where(c =>
        !char.IsControl(c) && char.GetUnicodeCategory(c) != UnicodeCategory.Format).Take(260)).ToArray());
    public static Uri? WebLink(string? value) => value is { Length: > 0 and <= 2048 } && !value.Any(c => char.IsControl(c) || c == '\\') &&
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Length != 0 && uri.UserInfo.Length == 0 && !uri.IsLoopback && !uri.IsUnc ? uri : null;

    async Task<byte[]> GetAsync(string url, int limit, CancellationToken token)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.UserAgent.ParseAdd("TokuControl-IntelStream/1.0");
        using var response = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("配信サイズが上限を超えました");
        using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var bytes = new MemoryStream(); var buffer = new byte[16384];
        int n;
        while ((n = await stream.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + n > limit) throw new InvalidDataException("配信サイズが上限を超えました");
            bytes.Write(buffer, 0, n);
        }
        return bytes.ToArray();
    }
    public async Task<IntelBatch> ReadAsync(IntelSource source, CancellationToken token)
    {
        if (source != IntelSource.HackerNews)
        {
            var url = source switch { IntelSource.JpCert => JpUrl, IntelSource.HuggingFace => HfUrl, _ => throw new ArgumentOutOfRangeException(nameof(source)) };
            return ParseFeed(source, await GetAsync(url, 4 * 1024 * 1024, token).ConfigureAwait(false));
        }
        using var ids = JsonDocument.Parse(await GetAsync(HnApi + "topstories.json", 100_000, token).ConfigureAwait(false));
        var top = ids.RootElement.EnumerateArray().Take(24).Where(v => v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out long id) && id > 0).Select(v => v.GetInt64()).Distinct().ToArray();
        int failed = 0; var stories = new List<IntelArticle>();
        foreach (var group in top.Chunk(4))
        {
            token.ThrowIfCancellationRequested();
            var parts = await Task.WhenAll(group.Select(async id =>
            {
                try { return ParseHn(id, await GetAsync(HnApi + "item/" + id.ToString(CultureInfo.InvariantCulture) + ".json", 250_000, token).ConfigureAwait(false)); }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or InvalidOperationException or ArgumentOutOfRangeException)
                { Interlocked.Increment(ref failed); return null; }
            })).ConfigureAwait(false);
            stories.AddRange(parts.OfType<IntelArticle>());
        }
        if (failed > 0 && stories.Count == 0) throw new IOException("HN投稿を取得できませんでした");
        return new(stories.OrderBy(a => a.Topic == "技術・話題").ToArray(), failed == 0 ? null : $"投稿 {failed} 件は取得不可");
    }
    static IntelArticle? ParseHn(long id, byte[] data)
    {
        using var doc = JsonDocument.Parse(data); var item = doc.RootElement;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var key) || key.GetInt64() != id ||
            !item.TryGetProperty("type", out var type) || type.GetString() != "story" ||
            item.TryGetProperty("dead", out var dead) && dead.ValueKind == JsonValueKind.True ||
            item.TryGetProperty("deleted", out var deleted) && deleted.ValueKind == JsonValueKind.True) return null;
        var title = Text(item.TryGetProperty("title", out var t) ? t.GetString() : null); if (title.Length == 0) return null;
        var discussion = "https://news.ycombinator.com/item?id=" + id.ToString(CultureInfo.InvariantCulture);
        var url = WebLink(item.TryGetProperty("url", out var u) ? u.GetString() : null)?.AbsoluteUri ?? discussion;
        DateTimeOffset? date = item.TryGetProperty("time", out var ts) && ts.TryGetInt64(out var sec) ? DateTimeOffset.FromUnixTimeSeconds(sec) : null;
        return new(IntelSource.HackerNews, title, url, date, discussion);
    }
    internal static IntelBatch ParseFeed(IntelSource source, byte[] bytes)
    {
        using var input = new MemoryStream(bytes);
        using var xml = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 });
        var doc = XDocument.Load(xml); var root = doc.Root;
        if (root == null || root.Name.LocalName is not ("rss" or "RDF") || !root.Elements().Any(e => e.Name.LocalName == "channel")) throw new InvalidDataException("RSS形式を確認できません");
        var list = new List<IntelArticle>(); int unusable = 0;
        foreach (var item in root.Descendants().Where(e => e.Name.LocalName == "item"))
        {
            string? Field(string name) => item.Elements().FirstOrDefault(e => e.Name.LocalName == name)?.Value;
            var title = Text(Field("title")); var url = WebLink(Field("link")?.Trim());
            if (title.Length == 0 || url == null) { unusable++; continue; }
            var date = DateTimeOffset.TryParse(Field("pubDate") ?? Field("date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var d) ? d : (DateTimeOffset?)null;
            list.Add(new(source, title, url.AbsoluteUri, date));
            if (list.Count == 48) break;
        }
        if (unusable != 0 && list.Count == 0) throw new InvalidDataException("読める見出しがありません");
        return new(list.DistinctBy(a => a.Key).OrderByDescending(a => a.Date).Take(32).ToArray(), unusable == 0 ? null : $"除外 {unusable} 件");
    }
}
