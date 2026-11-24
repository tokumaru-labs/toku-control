using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokuControl;

internal sealed record HackerGrowth(int Days = 0, DateOnly? LastDay = null, DateOnly[]? EarnedDates = null, DateOnly? LegacyThrough = null, DateOnly? RoundDay = null, Guid[]? RoundIds = null)
{
    [JsonIgnore] public int Scene => Days switch { 0 => 0, < 7 => 1, < 30 => 2, _ => 3 };
    [JsonIgnore] public string TraceName => Scene switch { 0 => "起動局", 1 => "信号同期", 2 => "経路追跡", _ => "管制網" };
    [JsonIgnore] public string SceneName => Scene switch { 0 => "作業机", 1 => "信号受信", 2 => "解析ラック", _ => "秘密基地" };
}

internal sealed class HackerJournal(string directory)
{
    readonly string file = Path.Combine(directory, "growth-v1.json");
    public HackerGrowth Read()
    {
        using var input = OpenExisting();
        if (input == null) return new();
        if (input.Length > 65536) throw new InvalidDataException("Invalid growth file size");
        var state = JsonSerializer.Deserialize<HackerGrowth>(input);
        if (state == null || state.Days is < 1 or > 365 || !state.LastDay.HasValue)
            throw new InvalidDataException("Invalid growth record");
        if (state.RoundDay.HasValue != (state.RoundIds is { Length: > 0 }) ||
            state.RoundIds is { } ids && (ids.Contains(Guid.Empty) || ids.Length != ids.Distinct().Count()))
            throw new InvalidDataException("Invalid round record");
        // Older summaries only knew the last earned date. Preserve their count and cutoff.
        if (state.EarnedDates == null) return state with { EarnedDates = [], LegacyThrough = state.LastDay };
        var dates = state.EarnedDates;
        if (dates.Length > state.Days || dates.Distinct().Count() != dates.Length ||
            (state.Days > dates.Length) != state.LegacyThrough.HasValue ||
            dates.Any(day => day > state.LastDay || day <= state.LegacyThrough) ||
            state.LastDay != (dates.Length > 0 ? dates.Max() : state.LegacyThrough))
            throw new InvalidDataException("Invalid earned dates");
        return state;
    }
    FileStream? OpenExisting()
    {
        try { return new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }
    public HackerGrowth Complete(DateOnly day, Guid? round = null)
    {
        Directory.CreateDirectory(directory);
        using var writer = ProfileWriteLock.Open(file);
        var latest = Read();
        var dates = latest.EarnedDates ?? [];
        var updated = latest;
        if (latest.Days < 365 && !(day <= latest.LegacyThrough) && !dates.Contains(day))
            updated = latest with { Days = latest.Days + 1, LastDay = latest.LastDay > day ? latest.LastDay : day, EarnedDates = [.. dates, day] };
        if (round is Guid id && !(day < latest.RoundDay))
        {
            Guid[] rounds = latest.RoundDay == day ? latest.RoundIds! : [];
            if (!rounds.Contains(id)) updated = updated with { RoundDay = day, RoundIds = [.. rounds, id] };
        }
        if (ReferenceEquals(updated, latest)) return latest;
        string temp = file + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            byte[] data = JsonSerializer.SerializeToUtf8Bytes(updated);
            if (data.Length > 65536) throw new InvalidDataException("Growth record exceeds read limit");
            File.WriteAllBytes(temp, data);
            File.Move(temp, file, true);
        }
        finally { try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        return updated;
    }
}

internal sealed class HackerRest(HackerJournal journal, TimeProvider time)
{
    long? started;
    const string CycleStopped = "長時間の中断でポモドーロを停止しました";
    DateTimeOffset scheduledEnd;
    Guid? pendingRound;
    public bool Cycling { get; private set; }
    public bool Focusing { get; private set; }
    public int TodayRounds => Growth.RoundDay == DateOnly.FromDateTime(time.GetLocalNow().DateTime) ? Growth.RoundIds?.Length ?? 0 : 0;
    public TraceHistory History { get; } = new();
    public int ElapsedSeconds => Math.Clamp(Minutes * 60 - (int)Math.Ceiling(Remaining.TotalSeconds), 0, Minutes * 60);
    public int Minutes { get; private set; } = 25;
    public bool Running => started.HasValue;
    public bool Finished { get; private set; }
    public bool PendingSave { get; private set; }
    public HackerGrowth Growth { get; private set; } = new();
    public string? Warning { get; private set; }
    public TimeSpan Remaining => started is long stamp
        ? TimeSpan.FromTicks(Math.Max(0, (TimeSpan.FromMinutes(Minutes) - time.GetElapsedTime(stamp)).Ticks)) : TimeSpan.Zero;
    static bool StorageError(Exception e) => e is IOException or InvalidDataException or UnauthorizedAccessException or JsonException;
    public void Load()
    {
        try { Growth = journal.Read(); Warning = null; }
        catch (Exception e) when (StorageError(e)) { Warning = "成長記録を読めません"; }
    }
    public bool Start(int minutes)
    {
        if (Running || PendingSave || minutes is < 5 or > 120 || minutes % 5 != 0) return false;
        Cycling = false;
        Begin(minutes, false, time.GetTimestamp(), time.GetUtcNow().AddMinutes(minutes));
        return true;
    }
    public bool StartPomodoro()
    {
        if (Running || PendingSave) return false;
        Cycling = true;
        Begin(20, true, time.GetTimestamp(), time.GetUtcNow().AddMinutes(20));
        return true;
    }
    void Begin(int minutes, bool focus, long stamp, DateTimeOffset end)
    {
        if (Warning == CycleStopped) Warning = null;
        started = stamp; scheduledEnd = end; Minutes = minutes; Focusing = focus;
        Finished = false; pendingRound = null;
        if (!focus) History.Start(minutes * 60);
    }
    public void Reset()
    {
        Advance();
        if (PendingSave) return;
        if (Running && !Focusing) History.Finish(ElapsedSeconds, false);
        started = null; Finished = false; Cycling = Focusing = false;
    }
    public void Advance()
    {
        if (started is not long stamp || Remaining > TimeSpan.Zero) return;
        long boundary = stamp + Minutes * 60L * time.TimestampFrequency;
        if (Cycling && time.GetElapsedTime(boundary) >= TimeSpan.FromMinutes(Focusing ? 5 : 20))
        {
            if (!Focusing) History.Finish(ElapsedSeconds, false);
            started = null; Cycling = Focusing = false;
            Warning = CycleStopped; return;
        }
        if (Focusing)
        {
            Begin(5, false, boundary, scheduledEnd.AddMinutes(5));
            return;
        }
        History.Finish(Minutes * 60, true);
        started = null; Finished = true; PendingSave = true;
        pendingRound = Cycling ? Guid.NewGuid() : null;
        RetrySave();
        if (Cycling && !PendingSave) Begin(20, true, boundary, scheduledEnd.AddMinutes(20));
        else Cycling = false;
    }
    public void RetrySave()
    {
        if (!PendingSave) return;
        try { Growth = journal.Complete(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(scheduledEnd, time.LocalTimeZone).DateTime), pendingRound); PendingSave = false; Warning = null; }
        catch (Exception e) when (StorageError(e)) { Warning = "成長を保存できません / 再保存"; }
    }
    public bool PrepareToExit()
    {
        Advance();
        if (PendingSave) RetrySave();
        return !PendingSave;
    }
}
