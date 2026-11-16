namespace DragonFit.Api.Game;

/// <summary>
/// The player's local calendar. A day stays open for edits until 4:00 the next morning,
/// when it is settled (plan B10).
/// </summary>
public readonly record struct DayClock(DateTime LocalNow)
{
    public const int SettleHour = 4;

    public static DayClock For(TimeProvider clock, string timeZoneId) =>
        new(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)).DateTime);

    public DateOnly Today => DateOnly.FromDateTime(LocalNow);

    /// <summary>Today, plus yesterday until 4:00.</summary>
    public bool IsOpen(DateOnly day) => day == Today || (day == Today.AddDays(-1) && LocalNow.Hour < SettleHour);

    /// <summary>The newest day that can be settled now.</summary>
    public DateOnly LastClosedDay => Today.AddDays(LocalNow.Hour >= SettleHour ? -1 : -2);

    public static DateOnly LocalDate(DateTime utc, string timeZoneId) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(timeZoneId)));
}
