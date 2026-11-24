using System.Diagnostics;
using System.IO;

namespace TokuControl;

internal static class ProfileWriteLock
{
    // Serialize read/modify/replace for one data file across app instances and login sessions.
    internal static FileStream Open(string dataFile)
    {
        var wait = Stopwatch.StartNew();
        while (true)
        {
            try { return new FileStream(dataFile + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) when ((ex.HResult & 0xFFFF) is 32 or 33 && wait.ElapsedMilliseconds < 400) { Thread.Sleep(10); }
        }
    }
}
