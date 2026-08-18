using System.Diagnostics;

namespace VoiceType.Core.Time;

public interface IClock
{
    /// <summary>Monotonic milliseconds since process start. Used for hold-duration checks.</summary>
    long ElapsedMs { get; }

    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    private static readonly Stopwatch Stopwatch = Stopwatch.StartNew();

    public long ElapsedMs => Stopwatch.ElapsedMilliseconds;
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
