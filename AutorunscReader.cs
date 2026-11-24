using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;

namespace TokuControl;

internal static class AutorunscReader
{
    // Offline pins of the Microsoft-signed x86 and x64 executables from the official Autoruns 14.3 ZIP.
    // Recheck the vendor signature and SHA-256 before admitting a new version.
    static readonly Dictionary<string, string> hashes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["autorunsc64.exe"] = "093D1C6B91D280C9644635C667753E0AA0E78C656EB4082147BF30364787D96F",
        ["autorunsc.exe"] = "A2394A11272AE38E7FB6EA862E783DAA6D704A3D9C73A51DFE978B7692DF6B1F"
    };

    internal static string LocalFile(string path)
    {
        if (path.Length < 4 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || path[2] != '\\' ||
            path.AsSpan(2).Contains(':') || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("この PC 上のファイルを選んでください。");
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        if (new DriveInfo(root).DriveType is not (DriveType.Fixed or DriveType.Removable))
            throw new InvalidDataException("ローカルドライブを選んでください。");
        string current = root;
        foreach (var part in full[root.Length..].Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            // Inspect from the root down before traversing each directory.
            if ((File.GetAttributes(current) & (FileAttributes.ReparsePoint | FileAttributes.Offline)) != 0)
                throw new InvalidDataException("リンクやオンライン専用ファイルは使用できません。");
        }
        return full;
    }

    internal static async Task<byte[]> ReadBounded(Stream source, int maximum, CancellationToken token)
    {
        using var bytes = new MemoryStream();
        var buffer = new byte[16384];
        int count;
        while ((count = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            if (bytes.Length + count > maximum) throw new InvalidDataException("読み取り上限を超えました。");
            bytes.Write(buffer, 0, count);
        }
        return bytes.ToArray();
    }

    internal static ProcessStartInfo StartInfo(string verifiedCopy)
    {
        var start = new ProcessStartInfo(verifiedCopy)
        {
            WorkingDirectory = Environment.SystemDirectory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        start.Environment["PATH"] = Environment.SystemDirectory;
        foreach (string argument in new[] { "-nobanner", "-a", "*", "-c" }) start.ArgumentList.Add(argument);
        return start;
    }

    internal static async Task<List<StartupItem>> Read(string path, bool collect, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string full = LocalFile(path);
        using var source = File.Open(full, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (!collect)
            return AutorunscSnapshot.Parse(await ReadBounded(source, AutorunscSnapshot.MaxBytes, token), token);

        using var identity = WindowsIdentity.GetCurrent();
        if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            throw new InvalidOperationException("通常権限で TOKU CONTROL を開き直してください。");
        var name = Path.GetFileName(full);
        if (!hashes.TryGetValue(name, out var expected))
            throw new InvalidDataException("Autorunsc 14.3 の autorunsc64.exe または autorunsc.exe を選んでください。");
        var binary = await ReadBounded(source, 6 * 1024 * 1024, token);
        if (!Convert.ToHexString(SHA256.HashData(binary)).Equals(expected, StringComparison.Ordinal))
            throw new InvalidDataException("確認済みの Microsoft 配布版 14.3 と一致しません。CSV 読込も利用できます。");

        // Keep executable-directory DLL lookup away from the selected download directory.
        var scratch = Directory.CreateTempSubdirectory("TokuControl-Autorunsc-");
        string exe = Path.Combine(scratch.FullName, name);
        try
        {
            await File.WriteAllBytesAsync(exe, binary, token);
            using var executableLock = File.Open(exe, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (!Convert.ToHexString(await SHA256.HashDataAsync(executableLock, token)).Equals(expected, StringComparison.Ordinal))
                throw new InvalidDataException("実行ファイルの照合に失敗しました。");
            token.ThrowIfCancellationRequested();
            using var process = Process.Start(StartInfo(exe)) ?? throw new InvalidOperationException("Autorunsc を起動できません。");
            using var stop = CancellationTokenSource.CreateLinkedTokenSource(token);
            stop.CancelAfter(TimeSpan.FromMinutes(2));
            void Kill()
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            using var kill = stop.Token.Register(Kill);
            async Task<byte[]> Drain(Stream stream, int limit)
            {
                try { return await ReadBounded(stream, limit, stop.Token).ConfigureAwait(false); }
                catch { Kill(); throw; }
            }
            var output = Drain(process.StandardOutput.BaseStream, AutorunscSnapshot.MaxBytes);
            var errors = Drain(process.StandardError.BaseStream, 65536);
            try
            {
                await Task.WhenAll(output, errors, process.WaitForExitAsync(stop.Token));
                stop.Token.ThrowIfCancellationRequested();
                if (process.ExitCode != 0 || HasDiagnostic(await errors))
                    throw new InvalidDataException("Autorunsc の取得が完了しませんでした。利用条件・アクセス範囲を確認してください。");
                return AutorunscSnapshot.Parse(await output, token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("取得を2分で中止しました。前回の結果を保持します。");
            }
            finally { Kill(); await process.WaitForExitAsync(); }
        }
        finally
        {
            try { File.Delete(exe); scratch.Delete(); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
    // A BOM or blank line on stderr isn't a diagnostic. Any other bytes make the snapshot incomplete.
    static bool HasDiagnostic(byte[] bytes) => bytes.Any(b => b is not (0 or 9 or 10 or 13 or 32 or 0xFF or 0xFE or 0xEF or 0xBB or 0xBF));
}
