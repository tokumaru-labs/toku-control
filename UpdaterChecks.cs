using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace TokuControl;

internal static class UpdaterChecks
{
    internal sealed class Backend : IAppUpdateBackend
    {
        public bool Available { get; set; } = true;
        public UpdateOffer? Offer { get; set; } = new("0.4.0", 70000000);
        public int Checks, Downloads, Applies;
        public Func<CancellationToken, Task>? Download;
        public Task<UpdateOffer?> CheckAsync() { Checks++; return Task.FromResult(Offer); }
        public Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancellation)
        { Downloads++; progress(50); return Download?.Invoke(cancellation) ?? Task.CompletedTask; }
        public void QueueRestart(UpdateOffer offer, string[] restartArgs) => Applies++;
    }
    public static async Task<Dictionary<string, bool>> Run()
    {
        var results = new Dictionary<string, bool>();
        var backend = new Backend(); var session = new UpdateSession(backend);
        int saves = 0, closes = 0;
        await session.CheckAsync();
        results["update check never downloads or installs"] = session.Phase == UpdatePhase.Available && backend.Downloads == 0 && backend.Applies == 0;
        await session.InstallAsync(false, () => saves++, () => closes++, []);
        results["update requires explicit Yes"] = backend.Downloads == 0 && backend.Applies == 0;
        backend.Download = async cancellation => await Task.Delay(10000, cancellation);
        var first = session.InstallAsync(true, () => saves++, () => closes++, []);
        await session.InstallAsync(true, () => saves++, () => closes++, []);
        await session.CheckAsync();
        session.Cancel(); await first;
        results["cancel and duplicate clicks never install"] = session.Phase == UpdatePhase.Cancelled && backend.Downloads == 1 && backend.Checks == 1 && backend.Applies == 0 && saves == 0 && closes == 0;
        backend.Download = _ => throw new IOException("Network failed");
        await session.InstallAsync(true, () => saves++, () => closes++, []);
        results["download failure keeps app running"] = session.Phase == UpdatePhase.Error && backend.Applies == 0 && saves == 0 && closes == 0;
        backend.Download = null;
        await session.InstallAsync(true, () => throw new InvalidOperationException("Settings save failed"), () => closes++, []);
        results["settings save failure blocks apply"] = backend.Applies == 0 && closes == 0;
        await session.InstallAsync(true, () => saves++, () => closes++, []);
        results["Yes saves then queues restart and closes"] = session.Phase == UpdatePhase.Installing && saves == 1 && backend.Applies == 1 && closes == 1;
        var current = new UpdateSession(new Backend { Offer = null }); await current.CheckAsync();
        results["up to date is handled"] = current.Phase == UpdatePhase.Current && current.Offer == null;
        var disabled = new Backend { Available = false }; var off = new UpdateSession(disabled); await off.CheckAsync();
        results["unpackaged builds never connect"] = off.Phase == UpdatePhase.Unavailable && disabled.Checks == 0;
        var stoppedBackend = new Backend(); var stopped = new UpdateSession(stoppedBackend); await stopped.CheckAsync(); stopped.Stop();
        await stopped.InstallAsync(true, () => saves++, () => closes++, []);
        results["closing prevents a late installation"] = stoppedBackend.Applies == 0 && stoppedBackend.Downloads == 0;
        var asset = new VelopackAsset { PackageId = GithubUpdates.PackageId, Version = SemanticVersion.Parse("0.4.0"), Type = VelopackAssetType.Full,
            FileName = "TokuControl-0.4.0-full.nupkg", Size = 100, SHA256 = new string('A', 64) };
        GithubUpdates.ValidateAsset(asset);
        bool Rejected() { try { GithubUpdates.ValidateAsset(asset); return false; } catch (InvalidDataException) { return true; } }
        asset.FileName = "../TokuControl-0.4.0-full.nupkg"; results["reject update path traversal"] = Rejected();
        asset.FileName = "TokuControl-0.4.0-full.nupkg"; asset.SHA256 = ""; results["require SHA256 metadata"] = Rejected();
        asset.SHA256 = new string('A', 64); asset.PackageId = "OtherApp"; results["reject wrong package identity"] = Rejected();
        asset.PackageId = GithubUpdates.PackageId; asset.Size = long.MaxValue; results["reject oversized package"] = Rejected();

        string temp = Path.Combine(Path.GetTempPath(), "toku-update-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string feed = Path.Combine(temp, "feed"); Directory.CreateDirectory(feed);
            string name = "TokuControl-0.4.0-full.nupkg"; byte[] contents = "deterministic checksum test package"u8.ToArray();
            File.WriteAllBytes(Path.Combine(feed, name), contents);
            var entry = new { PackageId = "TokuControl", Version = "0.4.0", Type = "Full", FileName = name, Size = contents.Length, SHA256 = Convert.ToHexString(SHA256.HashData(contents)), SHA1 = Convert.ToHexString(SHA1.HashData(contents)) };
            File.WriteAllText(Path.Combine(feed, "releases.win.json"), JsonSerializer.Serialize(new { Assets = new[] { entry } }));
            UpdateManager Manager(string version, string cache) => new(new SimpleFileSource(new DirectoryInfo(feed)),
                new UpdateOptions { ExplicitChannel = "win", AllowVersionDowngrade = false, MaximumDeltasBeforeFallback = -1 },
                new TestVelopackLocator("TokuControl", version, Path.Combine(temp, cache)));
            var real = new GithubUpdates(Manager("0.3.0", "download"));
            var offer = await real.CheckAsync();
            results["real feed discovers newer version"] = offer?.Version == "0.4.0";
            await real.DownloadAsync(offer!, _ => { }, CancellationToken.None);
            results["real download verifies and stores package"] = File.ReadAllBytes(Path.Combine(temp, "download", name)).SequenceEqual(contents);
            var corrupt = new GithubUpdates(Manager("0.3.0", "corrupt")); var corruptOffer = await corrupt.CheckAsync();
            File.WriteAllBytes(Path.Combine(feed, name), Enumerable.Repeat((byte)'x', contents.Length).ToArray());
            try { await corrupt.DownloadAsync(corruptOffer!, _ => { }, CancellationToken.None); results["real corrupted package rejected"] = false; }
            catch (Velopack.Exceptions.ChecksumFailedException) { results["real corrupted package rejected"] = !File.Exists(Path.Combine(temp, "corrupt", name)); }
            results["real feed never downgrades"] = await new GithubUpdates(Manager("0.5.0", "newer")).CheckAsync() == null;
        }
        finally { Directory.Delete(temp, true); }
        return results;
    }
}

public sealed partial class ConsoleWindow
{
    public async Task CaptureUpdateChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        var results = new Dictionary<string, bool>();
        try
        {
            var fake = new UpdaterChecks.Backend(); var session = new UpdateSession(fake);
            int prepared = 0, restarted = 0;
            var dialog = new UpdateWindow(session, settings, Save, () => prepared++, () => restarted++, []) { Owner = this };
            dialog.Show(); await session.CheckAsync(); await Task.Delay(150);
            Capture(dialog, Path.Combine(directory, "update-available.png"));
            var yes = Descendants<Button>(dialog).Single(b => (string)b.Content == "YES / UPDATE");
            results["offer visible with Yes"] = yes.IsVisible && session.Phase == UpdatePhase.Available;
            var panel = (FrameworkElement)dialog.Content;
            results["update buttons inside dialog"] = Descendants<Button>(dialog).Where(b => b.IsVisible).All(b => new Rect(new Point(), panel.RenderSize).Contains(new Rect(b.TranslatePoint(new Point(), panel), b.RenderSize)));
            var auto = Descendants<CheckBox>(dialog).Single(); auto.IsChecked = false; auto.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            results["auto-check preference persists"] = !new SettingsStore(store.DirectoryPath).Load().CheckUpdatesOnStartup;
            dialog.Close();
            results["Later never downloads"] = fake.Downloads == 0 && fake.Applies == 0;
            dialog = new UpdateWindow(session, settings, Save, () => prepared++, () => restarted++, []) { Owner = this };
            dialog.Show();
            Descendants<Button>(dialog).Single(b => (string)b.Content == "YES / UPDATE").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100);
            results["Yes button downloads and restarts"] = fake.Downloads == 1 && fake.Applies == 1 && prepared == 1 && restarted == 1;
            dialog.Owner = null; dialog.Close();
            results["passed"] = results.Values.All(v => v);
            File.WriteAllText(Path.Combine(directory, "update-ui-checks.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
            Application.Current.Shutdown(results["passed"] ? 0 : 1);
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "error.txt"), ex.ToString()); Application.Current.Shutdown(1); }
    }
}
