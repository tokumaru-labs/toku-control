using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace TokuControl;

internal sealed class IntelFixture : HttpMessageHandler
{
    internal readonly List<Uri> Calls = [];
    internal bool Fail;
    internal bool Oversize;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, CancellationToken token)
    {
        var uri = req.RequestUri!; Calls.Add(uri);
        if (Oversize)
        {
            var bytes = new ByteArrayContent(new byte[4 * 1024 * 1024 + 1]);
            bytes.Headers.ContentLength = 1; // A misleading header must not bypass the actual byte limit.
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = bytes });
        }
        const string rdf = """<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#" xmlns="http://purl.org/rss/1.0/" xmlns:dc="http://purl.org/dc/elements/1.1/"><channel/><item><title>テスト 注意喚起 &amp; Windows</title><link>https://example.org/alert</link><dc:date>2025-01-02T12:30:00+09:00</dc:date></item><item><title>Duplicate</title><link>https://example.org/alert</link></item><item><title>Unsafe</title><link>file:///C:/temp/unsafe.exe</link></item></rdf:RDF>""";
        const string rss = """<rss version="2.0"><channel><item><title>Fixture AI model</title><link>https://example.org/model</link><pubDate>Thu, 02 Jan 2025 03:30:00 GMT</pubDate></item></channel></rss>""";
        var data = uri.AbsoluteUri switch
        {
            IntelFeeds.JpUrl => rdf, IntelFeeds.HfUrl => rss,
            "https://hacker-news.firebaseio.com/v0/topstories.json" => "[10,11,12]",
            "https://hacker-news.firebaseio.com/v0/item/10.json" => """{"type":"story","id":10,"title":"AI &amp; security","time":1735788600,"url":"https://example.org/hn"}""",
            "https://hacker-news.firebaseio.com/v0/item/11.json" => """{"type":"story","id":11,"title":"hidden","dead":true}""",
            "https://hacker-news.firebaseio.com/v0/item/12.json" => """{"type":"story","id":12,"title":"Ask HN","url":"javascript:bad"}""",
            _ => throw new InvalidOperationException("Unexpected request: " + uri.Host)
        };
        return Task.FromResult(new HttpResponseMessage(Fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = new StringContent(data) });
    }
}

public sealed partial class ConsoleWindow
{
    sealed class DelayedFeed : IIntelReader
    {
        internal readonly TaskCompletionSource<IntelBatch> Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal CancellationToken Received;
        public Task<IntelBatch> ReadAsync(IntelSource s, CancellationToken token) { Received = token; return Gate.Task; }
    }
    internal async Task CaptureIntelChecks(string directory, bool live)
    {
        Directory.CreateDirectory(directory); var checks = new Dictionary<string, bool>();
        var statuses = new Dictionary<string, string>(); var counts = new Dictionary<string, int>();
        string? error = null;
        try
        {
            var fixture = new IntelFixture(); using var http = new HttpClient(fixture);
            var reader = live ? new IntelFeeds() : new IntelFeeds(http);
            ShowIntelStream(reader, live); var w = intelWindow!; nav["intel"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            checks["one visible contained taskbar window"] = ReferenceEquals(w, intelWindow) && w.IsVisible && w.ShowInTaskbar && WindowPlacement.WorkArea(w).Contains(WindowPlacement.NativeBounds(w));
            checks["no news request on open"] = fixture.Calls.Count == 0 && w.Online.IsChecked != true && w.Choices.Values.All(c => c.IsChecked != true);
            w.Choices[IntelSource.JpCert].IsChecked = true;
            checks["selecting while offline does not fetch"] = fixture.Calls.Count == 0 && w.Shown.Count == 0;
            var started = DateTimeOffset.UtcNow; w.Online.IsChecked = true; await w.WhenIdle();
            checks["selected source returns stories"] = w.Library.Feeds.TryGetValue(IntelSource.JpCert, out var jp) && jp.Articles.Length > 0;
            if (!live)
            {
                checks["only checked source requested"] = fixture.Calls.Count == 1 && fixture.Calls.Single().AbsoluteUri == IntelFeeds.JpUrl;
                checks["RDF namespace, dedupe, unsafe URLs, date"] = jp?.Articles is [var story] && story.Title.Contains("& Windows") && story.Date?.UtcDateTime.Hour == 3;
            }
            w.Choices[IntelSource.HackerNews].IsChecked = true; w.Choices[IntelSource.HuggingFace].IsChecked = true;
            await w.WhenIdle();
            foreach (var s in Enum.GetValues<IntelSource>())
            {
                statuses[s.ToString()] = w.SourceStatus(s);
                counts[s.ToString()] = w.Library.Feeds.TryGetValue(s, out var snap) ? snap.Articles.Length : 0;
                checks["received " + s] = counts[s.ToString()] > 0 && snap!.Fetched >= started;
            }
            if (!live)
            {
                var hn = w.Library.Feeds[IntelSource.HackerNews].Articles;
                checks["HN unsafe link falls back to discussion, dead item skipped"] = hn.Length == 2 && hn[0].Title == "AI & security" && hn[1].Url == hn[1].Discussion;
                checks["no story destination contacted"] = fixture.Calls.All(u => u.Host != "example.org" && u.Host != "news.ycombinator.com");
                bool rejectsDtd;
                try { IntelFeeds.ParseFeed(IntelSource.HuggingFace, Encoding.UTF8.GetBytes("<!DOCTYPE rss [<!ENTITY e SYSTEM 'file:///file'>]><rss><channel/></rss>")); rejectsDtd = false; } catch (System.Xml.XmlException) { rejectsDtd = true; }
                checks["DTD rejected"] = rejectsDtd;
                fixture.Oversize = true;
                try { await reader.ReadAsync(IntelSource.HuggingFace, default); checks["actual response size bounded"] = false; }
                catch (InvalidDataException) { checks["actual response size bounded"] = true; }
                finally { fixture.Oversize = false; }
                checks["links are HTTPS only"] = new[] { "file:///C:/x", "javascript:x", "http://example.org", "https://a@b.com", "https://localhost/x", @"\\server\file" }.All(s => IntelFeeds.WebLink(s) == null);
            }
            var firstKey = w.Shown.FirstOrDefault()?.Key; var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(9) && (w.Shown.FirstOrDefault()?.Key == firstKey || w.Shown.Select(a => a.Source).Distinct().Count() < 3))
                await Task.Delay(100); // Pump the WPF dispatcher. Only the real playback timer can advance this stream.
            checks["WPF playback timer advances through all checked sources"] = firstKey != null && w.Shown.FirstOrDefault()?.Key != firstKey && w.Shown.Select(a => a.Source).Distinct().Count() == 3;
            var row = w.FirstArticleButton; row?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var selected = w.SelectedArticle;
            int pausedCount = w.Shown.Count; await Task.Delay(1850);
            checks["row keeps the next WPF tick paused with article details"] = selected != null && w.Paused && pausedCount == w.Shown.Count && w.Shown.First().Key == selected.Key && w.Original.IsEnabled == live;
            if (selected != null && !w.Library.Saved.Any(a => a.Key == selected.Key)) w.Bookmark.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            checks["bookmark persists"] = selected != null && w.Library.Saved.Any(a => a.Key == selected.Key);
            w.UpdateLayout(); Capture(w, Path.Combine(directory, "intel-stream.png"));
            w.Choices[IntelSource.JpCert].IsChecked = false;
            checks["unchecked source leaves stream immediately"] = !w.Shown.Any(a => a.Source == IntelSource.JpCert);
            w.Online.IsChecked = false; var sent = fixture.Calls.Count;
            w.Cache.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); w.Advance();
            checks["offline cache browsable without requests"] = w.FirstArticleButton != null && w.Shown.Count == 0 && fixture.Calls.Count == sent;
            w.Close(); ShowIntelStream(reader, live); var reopened = intelWindow!;
            reopened.Saved.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            checks["reopen offline with saved article"] = reopened.Online.IsChecked != true && reopened.Choices.Values.All(b => b.IsChecked != true) &&
                reopened.FirstArticleButton != null && reopened.Library.Saved.Any(a => a.Key == selected?.Key) && fixture.Calls.Count == sent;
            if (!live)
            {
                var sameDirectory = Path.Combine(store.DirectoryPath, "two-readers-" + Guid.NewGuid().ToString("N"));
                var left = new IntelLibrary(sameDirectory); var right = new IntelLibrary(sameDirectory); left.Load(); right.Load();
                var one = new IntelArticle(IntelSource.JpCert, "one", "https://example.org/a", null);
                var two = new IntelArticle(IntelSource.HackerNews, "two", "https://example.org/b", null);
                left.Store(one.Source, new([one])); left.ToggleSaved(one); right.Store(two.Source, new([two])); right.ToggleSaved(two);
                var reopenedLibrary = new IntelLibrary(sameDirectory); reopenedLibrary.Load();
                checks["stale second window preserves other source and bookmark"] = reopenedLibrary.Feeds.Count == 2 && reopenedLibrary.Saved.Count == 2;
                left.ToggleSaved(one); var afterRemove = new IntelLibrary(sameDirectory); afterRemove.Load();
                checks["remove only intended bookmark across windows"] = afterRemove.Saved.Count == 1 && afterRemove.Saved[0].Key == two.Key;
                await Task.WhenAll(Task.Run(() => left.ToggleSaved(one)), Task.Run(() => right.Store(two.Source, new([two]))));
                afterRemove.Load();
                checks["overlapping writers retain both updates"] = left.Warning == null && right.Warning == null && afterRemove.Feeds.Count == 2 && afterRemove.Saved.Count == 2;
                fixture.Fail = true; reopened.Choices[IntelSource.JpCert].IsChecked = true; reopened.Online.IsChecked = true; await reopened.WhenIdle();
                checks["failure retains old cache and identifies it"] = reopened.SourceStatus(IntelSource.JpCert).Contains("取得失敗") &&
                    reopened.SourceStatus(IntelSource.JpCert).Contains("前回") && reopened.Library.Feeds[IntelSource.JpCert].Fetched == jp?.Fetched;
                reopened.Close();
                var delayed = new DelayedFeed(); ShowIntelStream(delayed); var slow = intelWindow!;
                slow.Choices[IntelSource.JpCert].IsChecked = true; slow.Online.IsChecked = true; var read = slow.WhenIdle();
                slow.Choices[IntelSource.JpCert].IsChecked = false;
                delayed.Gate.SetResult(new IntelBatch([new(IntelSource.JpCert, "late data", "https://example.org/late", null)])); await read;
                checks["revoked request cancelled and late result discarded"] = delayed.Received.IsCancellationRequested && slow.Shown.Count == 0 && slow.Library.Feeds[IntelSource.JpCert].Fetched == jp?.Fetched;
                slow.Close();
            }
            else reopened.Close();
        }
        catch (Exception ex) { error = ex.ToString(); }
        var passed = error == null && checks.Count > 10 && checks.Values.All(v => v);
        File.WriteAllText(Path.Combine(directory, "intel-checks.json"), JsonSerializer.Serialize(new { passed, live, checks, counts, statuses, error }, new JsonSerializerOptions { WriteIndented = true }));
        Application.Current.Shutdown(passed ? 0 : 1);
    }
}
