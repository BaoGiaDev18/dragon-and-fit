namespace DragonFit.Api.Game;

/// <summary>
/// Development only: a clock that can be moved forward to play through several days (settlement,
/// streaks, death) without waiting. Production always uses TimeProvider.System.
/// </summary>
public class DevClock : TimeProvider
{
    public TimeSpan Offset { get; set; }
    public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Offset;
}
