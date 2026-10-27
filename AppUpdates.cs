using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.RegularExpressions;
using Velopack;
using Velopack.Sources;

namespace TokuControl;

internal record UpdateOffer(string Version, long Bytes);
internal interface IAppUpdateBackend
{
    bool Available { get; }
    Task<UpdateOffer?> CheckAsync();
    Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancellation);
    void QueueRestart(UpdateOffer offer, string[] restartArgs);
}

internal sealed class GithubUpdates : IAppUpdateBackend
{
    public const string Repository = "https://github.com/tokumaru-labs/toku-control";
    public const string PackageId = "TokuControl";
    public static string AppVersion => typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    readonly UpdateManager manager;
    UpdateInfo? pending;

    public GithubUpdates() : this(new UpdateManager(new GithubSource(Repository, null, false, new UpdateDownloader()),
        new UpdateOptions { ExplicitChannel = "win", AllowVersionDowngrade = false, MaximumDeltasBeforeFallback = -1 })) { }
    internal GithubUpdates(UpdateManager manager) => this.manager = manager;
    public bool Available => manager.IsInstalled && manager.AppId == PackageId;
    public async Task<UpdateOffer?> CheckAsync()
    {
        if (!Available) throw new InvalidOperationException("This build is not an update-enabled portable package.");
        pending = null;
        var update = await manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(30));
        if (update == null) return null;
        var asset = update.TargetFullRelease;
        ValidateAsset(asset);
        if (asset.Version <= manager.CurrentVersion || asset.Version.IsPrerelease) return null;
        pending = update;
        return new(asset.Version.ToString(), asset.Size);
    }
    internal static void ValidateAsset(VelopackAsset asset)
    {
        if (asset.PackageId != PackageId || asset.Type != VelopackAssetType.Full
            || asset.Size is <= 0 or > 536870912
            || !Regex.IsMatch(asset.SHA256 ?? "", "\\A[0-9a-fA-F]{64}\\z")
            || !Regex.IsMatch(asset.FileName ?? "", "\\ATokuControl-[0-9]+\\.[0-9]+\\.[0-9]+-full\\.nupkg\\z"))
            throw new InvalidDataException("Invalid update package metadata.");
    }
    UpdateInfo Pending(UpdateOffer offer)
    {
        if (pending == null || pending.TargetFullRelease.Version.ToString() != offer.Version || pending.TargetFullRelease.Size != offer.Bytes)
            throw new InvalidOperationException("Check for this update again.");
        return pending;
    }
    public Task DownloadAsync(UpdateOffer offer, Action<int> progress, CancellationToken cancellation)
        => manager.DownloadUpdatesAsync(Pending(offer), progress, cancellation);
    public void QueueRestart(UpdateOffer offer, string[] restartArgs)
        => manager.WaitExitThenApplyUpdates(Pending(offer).TargetFullRelease, silent: false, restart: true, restartArgs: restartArgs);

    sealed class UpdateDownloader : HttpClientFileDownloader
    {
        protected override HttpClient CreateHttpClient(IDictionary<string, string>? headers = null, double timeout = 30)
            => base.CreateHttpClient(headers, Math.Min(timeout, 10));
    }
}

internal enum UpdatePhase { Idle, Checking, Available, Current, Downloading, Installing, Cancelled, Error, Unavailable }

internal sealed class UpdateSession(IAppUpdateBackend? backend)
{
    public UpdatePhase Phase { get; private set; } = backend?.Available == true ? UpdatePhase.Idle : UpdatePhase.Unavailable;
    public UpdateOffer? Offer { get; private set; }
    public string Message { get; private set; } = "";
    public int Progress { get; private set; }
    public bool Busy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading or UpdatePhase.Installing;
    public event Action? Changed;
    CancellationTokenSource? download;
    bool stopped;

    void Set(UpdatePhase phase, string message = "") { Phase = phase; Message = message; Changed?.Invoke(); }
    public async Task CheckAsync()
    {
        if (Busy || stopped || backend?.Available != true) return;
        Offer = null; Set(UpdatePhase.Checking);
        try
        {
            var offer = await backend.CheckAsync();
            if (stopped) return;
            Offer = offer; Set(offer == null ? UpdatePhase.Current : UpdatePhase.Available);
        }
        catch (Exception ex) { if (!stopped) Set(UpdatePhase.Error, ErrorMessage(ex)); }
    }
    public async Task InstallAsync(bool consent, Action beforeRestart, Action shutdown, string[] restartArgs)
    {
        if (!consent || Busy || stopped || Offer == null || backend == null) return;
        var offer = Offer;
        download = new CancellationTokenSource(); var cancellation = download.Token;
        Progress = 0; Set(UpdatePhase.Downloading);
        var progress = new Progress<int>(value => { if (Phase == UpdatePhase.Downloading) { Progress = Math.Clamp(value, 0, 100); Changed?.Invoke(); } });
        try
        {
            await backend.DownloadAsync(offer, value => ((IProgress<int>)progress).Report(value), cancellation);
            cancellation.ThrowIfCancellationRequested();
            beforeRestart(); // Save settings and reject an in-flight audio/drive operation before starting the updater.
            cancellation.ThrowIfCancellationRequested();
            Set(UpdatePhase.Installing);
            backend.QueueRestart(offer, restartArgs);
            shutdown();
        }
        catch (OperationCanceledException) { Set(UpdatePhase.Cancelled); }
        catch (Exception ex) { Set(UpdatePhase.Error, ErrorMessage(ex)); }
        finally { download.Dispose(); download = null; }
    }
    public void Cancel() => download?.Cancel();
    public void Stop() { stopped = true; Cancel(); }
    static string ErrorMessage(Exception error) => error switch
    {
        Velopack.Exceptions.ChecksumFailedException => "ファイルの検証に失敗しました。更新せず、現在のバージョンを保持しました。",
        UnauthorizedAccessException => "書き込みできません。アプリのフォルダーの権限を確認してください。",
        HttpRequestException or TaskCanceledException or TimeoutException => "更新を取得できませんでした。接続を確認し、後でもう一度お試しください。",
        InvalidOperationException => error.Message,
        _ => "更新できませんでした。現在のバージョンを続けて使えます。 / " + error.GetType().Name
    };
}
