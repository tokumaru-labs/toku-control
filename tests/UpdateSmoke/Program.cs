using System.Diagnostics;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace TokuControl;

// Developer-only runner outside the disposable install. Uses production update
// code and the native updater; no local-source override is shipped in the app.
internal static class Program
{
    static async Task<int> Main(string[] args)
    {
        if (args.Length != 4) return 2;
        var root = Path.GetFullPath(args[0]); var feed = Path.GetFullPath(args[1]);
        var state = Path.GetFullPath(args[2]); var captures = Path.GetFullPath(args[3]);
        var process = new SmokeProcess(Path.Combine(root, "current", "TokuControl.exe"));
        var locator = new WindowsVelopackLocator(process, null);
        var manager = new UpdateManager(new SimpleFileSource(new DirectoryInfo(feed)),
            new UpdateOptions { ExplicitChannel = "win", AllowVersionDowngrade = false, MaximumDeltasBeforeFallback = -1 }, locator);
        var session = new UpdateSession(new GithubUpdates(manager));
        await session.CheckAsync();
        if (session.Phase != UpdatePhase.Available) throw new Exception("No candidate update: " + session.Message);
        Console.WriteLine($"Native update: {manager.CurrentVersion} -> {session.Offer!.Version}");
        bool queued = false;
        await session.InstallAsync(true,
            () => File.WriteAllText(Path.Combine(state, "prepared.txt"), session.Offer.Version),
            () => queued = true,
            ["--preview", "--data-dir", state, "--gallery-dir", captures]);
        if (!queued || session.Phase != UpdatePhase.Installing) throw new Exception("Update was not queued: " + session.Message);
        return 0; // Update.exe waits for this real PID before replacing current/.
    }

    sealed class SmokeProcess(string path) : IProcessImpl
    {
        public string GetCurrentProcessPath() => path;
        public uint GetCurrentProcessId() => (uint)Environment.ProcessId;
        public void Exit(int exitCode) => Environment.Exit(exitCode);
        public void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow)
        {
            var start = new ProcessStartInfo(exePath) { WorkingDirectory = workDir, UseShellExecute = false, CreateNoWindow = !showWindow };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var child = Process.Start(start) ?? throw new IOException("Could not start native updater.");
        }
    }
}
