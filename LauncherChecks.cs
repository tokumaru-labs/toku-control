using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace TokuControl;

internal static class LauncherChecks
{
    internal static readonly string[] Targets = ["EXPLORER", "NOTEPAD", "CALC", "TASK MANAGER", "CONTROL PANEL", "TASK SCHEDULER", "STARTUP APPS", "COMMAND PROMPT", "POWERSHELL"];
    internal static readonly string[] AdminTargets = ["NOTEPAD", "TASK MANAGER", "TASK SCHEDULER", "COMMAND PROMPT", "POWERSHELL"];

    public static Dictionary<string, bool> Run()
    {
        var results = new Dictionary<string, bool>();
        var requests = new List<ProcessStartInfo>();
        var launcher = new QuickLauncher(requests.Add);
        foreach (string target in Targets)
        {
            requests.Clear();
            var normal = launcher.Launch(target, false);
            results[target + " normal launch"] = normal.Level == "OK" && requests.Count == 1
                && requests[0].UseShellExecute && requests[0].Verb == "";
            requests.Clear();
            var elevated = launcher.Launch(target, false, true);
            bool supportsAdmin = AdminTargets.Contains(target);
            results[target + " elevation policy"] = supportsAdmin
                ? elevated.Level == "OK" && requests.Count == 1 && requests[0].Verb == "runas" && requests[0].UseShellExecute
                : elevated.Level == "ERROR" && requests.Count == 0;
            requests.Clear();
            launcher.Launch(target, true); launcher.Launch(target, true, true);
            results[target + " preview blocks both modes"] = requests.Count == 0;
        }
        var cmd = QuickLauncher.CreateStartInfo("COMMAND PROMPT");
        var ps = QuickLauncher.CreateStartInfo("POWERSHELL");
        results["shells use Windows executables"] = cmd.FileName == Path.Combine(Environment.SystemDirectory, "cmd.exe")
            && ps.FileName == Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe")
            && File.Exists(cmd.FileName) && File.Exists(ps.FileName);
        results["shells open home without injected commands"] = new[] { cmd, ps }.All(info => info.ArgumentList.Count == 0
            && info.Arguments == "" && info.WorkingDirectory == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        results["classic panel target preserved"] = QuickLauncher.CreateStartInfo("CONTROL PANEL").ArgumentList.SequenceEqual(["shell:ControlPanelFolder"]);
        results["scheduler target preserved"] = QuickLauncher.CreateStartInfo("TASK SCHEDULER", true).ArgumentList.SequenceEqual([Path.Combine(Environment.SystemDirectory, "taskschd.msc")]);
        requests.Clear();
        results["unknown target rejected"] = launcher.Launch("arbitrary.exe", false).Level == "ERROR" && requests.Count == 0;
        int attempts = 0;
        var cancelled = new QuickLauncher(_ => { attempts++; throw new Win32Exception(1223); }).Launch("COMMAND PROMPT", false, true);
        results["UAC cancellation is informational without retry"] = attempts == 1 && cancelled.Level == "INFO" && cancelled.Message.Contains("cancelled");
        attempts = 0;
        var denied = new QuickLauncher(_ => { attempts++; throw new Win32Exception(5); }).Launch("POWERSHELL", false, true);
        results["access denied is an error without retry"] = attempts == 1 && denied.Level == "ERROR";
        results["failure does not poison later launches"] = launcher.Launch("POWERSHELL", false).Level == "OK";
        return results;
    }
}

public sealed partial class ConsoleWindow
{
    // Exercise the actual button and menu handlers with a recording process starter.
    // Native capture must never open tools or request elevation on the desktop.
    bool CheckLauncherActions()
    {
        var original = launcher;
        var requests = new List<ProcessStartInfo>();
        launcher = new QuickLauncher(requests.Add);
        try
        {
            var buttons = Descendants<Button>(panels["launcher"]).ToArray();
            if (!buttons.Select(b => (string)b.Content).SequenceEqual(LauncherChecks.Targets)) return false;
            foreach (var button in buttons)
            {
                string target = (string)button.Content;
                requests.Clear(); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (preview ? requests.Count != 0 : requests.Count != 1 || requests[0].Verb != "") return false;
                bool admin = LauncherChecks.AdminTargets.Contains(target);
                if ((button.ContextMenu != null) != admin) return false;
                if (!admin) continue;
                var menu = button.ContextMenu!;
                if (menu.Items.Count != 2) return false;
                foreach (int index in new[] { 0, 1 })
                {
                    requests.Clear();
                    ((MenuItem)menu.Items[index]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    if (preview ? requests.Count != 0 : requests.Count != 1
                        || requests[0].Verb != (index == 1 ? "runas" : "")
                        || requests[0].FileName != QuickLauncher.CreateStartInfo(target).FileName) return false;
                }
            }
            launcher = new QuickLauncher(_ => throw new Win32Exception(1223));
            var shell = buttons.Single(b => (string)b.Content == "COMMAND PROMPT");
            ((MenuItem)shell.ContextMenu!.Items[1]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            return logs.Lines.Last().Contains(preview ? "preview / launcher disabled" : "launch cancelled");
        }
        finally { launcher = original; }
    }
}
