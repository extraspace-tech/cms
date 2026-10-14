using ExtraSpace.Engines;

namespace ExtraSpace.Engines.Tests;

public sealed class SiteClockTests
{
    private static readonly TimeZoneInfo Sg = SiteClock.Zone("Asia/Singapore");
    private static readonly TimeZoneInfo My = SiteClock.Zone("Asia/Kuala_Lumpur");
    private static readonly TimeZoneInfo Kr = SiteClock.Zone("Asia/Seoul");

    private static DateTimeOffset At(string utc) => DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public void Z1_a_UTC_instant_in_Singapore_time()
    {
        var local = SiteClock.ToLocal(At("2026-10-07T16:30:00Z"), Sg);
        local.Should().Be(new DateTime(2026, 10, 8, 0, 30, 0));
        local.DayOfWeek.Should().Be(DayOfWeek.Thursday);
        SiteClock.Today(At("2026-10-07T16:30:00Z"), Sg).Should().Be(new DateOnly(2026, 10, 8));
    }

    [Theory]
    [InlineData(540, "2026-10-08T01:00:00Z")]   // Z2
    [InlineData(1440, "2026-10-08T16:00:00Z")]  // Z3: 24:00 is the next midnight
    public void Z2_Z3_a_Singapore_wall_time_as_UTC(int minutes, string expected)
    {
        var utc = SiteClock.LocalToUtc(new DateOnly(2026, 10, 8), minutes, Sg);
        utc.Should().Be(At(expected).UtcDateTime);
        utc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void Malaysia_is_the_same_as_Singapore() =>
        SiteClock.LocalToUtc(new DateOnly(2026, 10, 8), 540, My).Should().Be(At("2026-10-08T01:00:00Z").UtcDateTime);

    [Fact]
    public void Korea_is_one_hour_ahead()
    {
        SiteClock.LocalToUtc(new DateOnly(2026, 10, 8), 540, Kr).Should().Be(At("2026-10-08T00:00:00Z").UtcDateTime);
        SiteClock.ToLocal(At("2026-10-07T15:30:00Z"), Kr).Should().Be(new DateTime(2026, 10, 8, 0, 30, 0));
        SiteClock.Today(At("2026-10-07T14:59:00Z"), Kr).Should().Be(new DateOnly(2026, 10, 7));
    }
}
