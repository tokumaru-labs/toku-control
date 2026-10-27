using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace TokuControl;

internal sealed class QuickLauncher
{
    readonly Action<ProcessStartInfo> start;

    public QuickLauncher(Action<ProcessStartInfo>? start = null)
        => this.start = start ?? (info => { using var process = Process.Start(info); });

    // Shell/Settings targets may reuse an existing unelevated process. Only offer
    // elevation for executables that support a separate administrator launch.
    public static bool SupportsAdmin(string target)
        => target is "COMMAND PROMPT" or "POWERSHELL" or "NOTEPAD" or "TASK MANAGER" or "TASK SCHEDULER";

    public static ProcessStartInfo CreateStartInfo(string target, bool administrator = false)
    {
        if (administrator && !SupportsAdmin(target))
            throw new ArgumentException("Administrator launch is unavailable for this target.", nameof(target));
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        ProcessStartInfo info = target switch
        {
            "EXPLORER" => new(explorer),
            "NOTEPAD" => new(Path.Combine(Environment.SystemDirectory, "notepad.exe")),
            "CALC" => new(Path.Combine(Environment.SystemDirectory, "calc.exe")),
            "TASK MANAGER" => new(Path.Combine(Environment.SystemDirectory, "Taskmgr.exe")),
            "CONTROL PANEL" => new(explorer) { ArgumentList = { "shell:ControlPanelFolder" } },
            "TASK SCHEDULER" => new(Path.Combine(Environment.SystemDirectory, "mmc.exe")) { ArgumentList = { Path.Combine(Environment.SystemDirectory, "taskschd.msc") } },
            "STARTUP APPS" => new("ms-settings:startupapps"),
            "COMMAND PROMPT" => new(Path.Combine(Environment.SystemDirectory, "cmd.exe")),
            "POWERSHELL" => new(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")),
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        info.UseShellExecute = true;
        info.Verb = administrator ? "runas" : "";
        if (target is "COMMAND PROMPT" or "POWERSHELL")
            info.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return info;
    }

    public (string Level, string Message) Launch(string target, bool preview, bool administrator = false)
    {
        if (preview) return ("INFO", "preview / launcher disabled");
        string label = target.ToLowerInvariant() + (administrator ? " / administrator" : "");
        try
        {
            start(CreateStartInfo(target, administrator));
            return ("OK", label + " launch request accepted");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return ("INFO", label + " launch cancelled");
        }
        catch (Exception ex)
        {
            return ("ERROR", label + " launch failed / " + ex.GetType().Name);
        }
    }
}
