// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Import.TrackingCsv;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The zone a sheet's times were written in: which names stand for one, and what a date and time
/// on that zone's clocks is as an instant — on an ordinary day, and in the two hours a year where
/// the clocks themselves do not give one answer.
/// </summary>
/// <remarks>
/// Every date here is invented. The clock changes are the real ones of the zones named, for 2026:
/// Bucharest goes forward at 03:00 on 29 March and back at 04:00 on 25 October; New York goes
/// forward at 02:00 on 8 March and back at 02:00 on 1 November.
/// </remarks>
public class TrackingCsvZonesTests
{
    private static TimeZoneInfo Zone(string name)
    {
        TrackingCsvZones.TryFind(name, out var zone).ShouldBeTrue(name);
        return zone!;
    }

    private static DateTimeOffset Utc(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.Zero);

    private static TrackingCsvWallClock On(string zone, int year, int month, int day, int hour, int minute) =>
        TrackingCsvZones.Resolve(new DateTime(year, month, day, hour, minute, 0), Zone(zone));

    [Theory]
    [InlineData("Europe/Bucharest")]
    [InlineData("Asia/Kolkata")]
    [InlineData("America/New_York")]
    [InlineData("America/Argentina/Buenos_Aires")]
    [InlineData("UTC")]
    public void A_name_the_zone_database_and_a_browser_both_use_stands_for_a_zone(string name)
    {
        TrackingCsvZones.TryFind(name, out var zone).ShouldBeTrue();
        zone.ShouldNotBeNull();
        // Answered under the name it was asked by, so what is echoed to a reviewer is what they chose.
        zone.Id.ShouldBe(name);
    }

    /// <remarks>
    /// One browser engine family reports and lists these old spellings and not the new ones;
    /// another does the reverse; and a host may carry either or both. Each pair is asked for both
    /// ways round, so whichever spelling this host lacks is the one the fallback is proved on —
    /// and the pair is compared on a summer and a winter instant, so "found" cannot mean "found
    /// some other zone".
    /// </remarks>
    [Fact]
    public void A_place_the_zone_database_has_respelled_is_the_same_zone_under_either_spelling()
    {
        TrackingCsvZones.RespelledPlaces.Count.ShouldBe(18);
        var summer = new DateTime(2026, 7, 12, 14, 5, 0);
        var winter = new DateTime(2026, 1, 12, 14, 5, 0);

        foreach (var (old, current) in TrackingCsvZones.RespelledPlaces)
        {
            TrackingCsvZones.TryFind(old, out var byOld).ShouldBeTrue(old);
            TrackingCsvZones.TryFind(current, out var byCurrent).ShouldBeTrue(current);

            TrackingCsvZones.Resolve(summer, byOld!).ShouldBe(TrackingCsvZones.Resolve(summer, byCurrent!), old);
            TrackingCsvZones.Resolve(winter, byOld!).ShouldBe(TrackingCsvZones.Resolve(winter, byCurrent!), old);
        }

        // The ones whole countries depend on, by the instant and not only by agreement with their
        // pair: India is five and a half hours ahead all year, Ukraine three in summer.
        On("Asia/Calcutta", 2026, 7, 12, 14, 5).At.ShouldBe(Utc(2026, 7, 12, 8, 35));
        On("Europe/Kiev", 2026, 7, 12, 14, 5).At.ShouldBe(Utc(2026, 7, 12, 11, 5));
        On("America/Buenos_Aires", 2026, 7, 12, 14, 5).At.ShouldBe(Utc(2026, 7, 12, 17, 5));
        On("Asia/Saigon", 2026, 7, 12, 14, 5).At.ShouldBe(Utc(2026, 7, 12, 7, 5));
    }

    [Theory]
    [InlineData("europe/kiev")]
    [InlineData("Europe/Kiev2")]
    [InlineData("right/Europe/Kiev")]
    public void A_name_that_only_resembles_a_respelled_place_is_not_one(string name)
    {
        // The twin of the case above: the fallback is for the exact spellings, not a looser lookup.
        TrackingCsvZones.TryFind(name, out _).ShouldBeFalse();
    }

    [Fact]
    public void A_reading_at_the_edge_of_the_calendar_whose_instant_falls_off_it_is_answered_and_not_thrown()
    {
        // East of Greenwich the instant is before the reading: on the first day there is, before
        // the first instant there is. West, after the last.
        TrackingCsvZones.Resolve(new DateTime(1, 1, 1, 0, 30, 0), Zone("Europe/Bucharest")).Kind
            .ShouldBe(TrackingCsvWallClockKind.OffTheCalendar);
        TrackingCsvZones.Resolve(new DateTime(9999, 12, 31, 23, 30, 0), Zone("America/New_York")).Kind
            .ShouldBe(TrackingCsvWallClockKind.OffTheCalendar);

        // The twins: the same two readings where the offset carries them inward are ordinary.
        TrackingCsvZones.Resolve(new DateTime(1, 1, 1, 0, 30, 0), Zone("America/New_York")).Kind
            .ShouldBe(TrackingCsvWallClockKind.Once);
        TrackingCsvZones.Resolve(new DateTime(9999, 12, 31, 23, 30, 0), Zone("Asia/Kolkata")).Kind
            .ShouldBe(TrackingCsvWallClockKind.Once);
        var utc = TrackingCsvZones.Resolve(new DateTime(9999, 12, 31, 23, 30, 0), Zone("UTC"));
        utc.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        utc.At.ShouldBe(new DateTimeOffset(9999, 12, 31, 23, 30, 0, TimeSpan.Zero));
    }

    /// <remarks>
    /// The first four are the ones that matter: the framework on this platform resolves each of
    /// them, so it is the rule here that refuses them and not the lookup. <c>localtime</c> is the
    /// host's own zone — the reading the option exists to keep out — and the Windows spelling
    /// would make the same request mean something on one host and nothing on another.
    /// </remarks>
    [Theory]
    [InlineData("localtime")]
    [InlineData("posixrules")]
    [InlineData("Factory")]
    [InlineData("GTB Standard Time")]
    [InlineData("right/Europe/Bucharest")]
    [InlineData("posix/Europe/Bucharest")]
    [InlineData("Mars/Olympus_Mons")]
    [InlineData("../../etc/hostname")]
    [InlineData("Europe/Bucharest ")]
    [InlineData("Europe/Bucharest;UTC")]
    [InlineData("Europe")]
    [InlineData("+03:00")]
    [InlineData("")]
    [InlineData(null)]
    public void A_name_that_is_not_a_place_in_the_zone_database_stands_for_nothing(string? name)
    {
        TrackingCsvZones.TryFind(name, out var zone).ShouldBeFalse();
        zone.ShouldBeNull();
    }

    [Fact]
    public void A_name_longer_than_any_zone_has_is_not_looked_up()
    {
        var name = "Europe/" + new string('a', TrackingCsvZones.MaxNameLength);
        TrackingCsvZones.TryFind(name, out _).ShouldBeFalse();
    }

    [Fact]
    public void An_ordinary_reading_is_one_instant_at_the_offset_the_zone_kept_on_that_date()
    {
        // The same clock reading, in summer and in winter: three hours ahead and then two. Which
        // is why the offset is asked per reading and never once for a sheet.
        var summer = On("Europe/Bucharest", 2026, 7, 12, 14, 5);
        summer.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        summer.At.ShouldBe(Utc(2026, 7, 12, 11, 5));

        var winter = On("Europe/Bucharest", 2026, 1, 12, 14, 5);
        winter.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        winter.At.ShouldBe(Utc(2026, 1, 12, 12, 5));

        // West of Greenwich the instant is later than the reading, and can be the next day.
        var evening = On("America/New_York", 2026, 7, 12, 22, 30);
        evening.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        evening.At.ShouldBe(Utc(2026, 7, 13, 2, 30));
    }

    [Fact]
    public void A_reading_the_clocks_skipped_has_no_instant()
    {
        // 03:00 to 03:59 on the night the clocks go forward: no clock in the zone showed it.
        On("Europe/Bucharest", 2026, 3, 29, 3, 0).Kind.ShouldBe(TrackingCsvWallClockKind.Never);
        On("Europe/Bucharest", 2026, 3, 29, 3, 30).Kind.ShouldBe(TrackingCsvWallClockKind.Never);
        On("Europe/Bucharest", 2026, 3, 29, 3, 59).Kind.ShouldBe(TrackingCsvWallClockKind.Never);

        // The minute before and the minute after are ordinary, a minute apart as instants although
        // an hour and a minute apart as readings.
        var before = On("Europe/Bucharest", 2026, 3, 29, 2, 59);
        before.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        before.At.ShouldBe(Utc(2026, 3, 29, 0, 59));
        var after = On("Europe/Bucharest", 2026, 3, 29, 4, 0);
        after.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        after.At.ShouldBe(Utc(2026, 3, 29, 1, 0));

        // The same rule where the change is at another hour.
        On("America/New_York", 2026, 3, 8, 2, 30).Kind.ShouldBe(TrackingCsvWallClockKind.Never);
    }

    [Fact]
    public void A_reading_the_clocks_showed_twice_is_the_first_time_they_showed_it()
    {
        // 03:00 to 03:59 on the night the clocks go back happens twice. The first passage is still
        // on summer time, three hours ahead; the second, an hour later, is on winter time.
        var repeated = On("Europe/Bucharest", 2026, 10, 25, 3, 30);
        repeated.Kind.ShouldBe(TrackingCsvWallClockKind.Twice);
        repeated.At.ShouldBe(Utc(2026, 10, 25, 0, 30));

        On("Europe/Bucharest", 2026, 10, 25, 3, 0).At.ShouldBe(Utc(2026, 10, 25, 0, 0));
        On("Europe/Bucharest", 2026, 10, 25, 3, 59).Kind.ShouldBe(TrackingCsvWallClockKind.Twice);

        // Either side of the repeated hour there is one answer again.
        var before = On("Europe/Bucharest", 2026, 10, 25, 2, 59);
        before.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        before.At.ShouldBe(Utc(2026, 10, 24, 23, 59));
        var after = On("Europe/Bucharest", 2026, 10, 25, 4, 0);
        after.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        after.At.ShouldBe(Utc(2026, 10, 25, 2, 0));

        // West of Greenwich the first passage is still the one further from winter time.
        var west = On("America/New_York", 2026, 11, 1, 1, 30);
        west.Kind.ShouldBe(TrackingCsvWallClockKind.Twice);
        west.At.ShouldBe(Utc(2026, 11, 1, 5, 30));
    }

    [Fact]
    public void A_zone_that_never_changes_its_clocks_has_neither_edge()
    {
        // The readings Bucharest skips and repeats are ordinary in a zone with one offset all year,
        // five and a half hours ahead.
        var spring = On("Asia/Kolkata", 2026, 3, 29, 3, 30);
        spring.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        spring.At.ShouldBe(Utc(2026, 3, 28, 22, 0));

        var autumn = On("Asia/Kolkata", 2026, 10, 25, 3, 30);
        autumn.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        autumn.At.ShouldBe(Utc(2026, 10, 24, 22, 0));

        var utc = On("UTC", 2026, 3, 29, 3, 30);
        utc.Kind.ShouldBe(TrackingCsvWallClockKind.Once);
        utc.At.ShouldBe(Utc(2026, 3, 29, 3, 30));
    }

    [Fact]
    public void What_kind_the_date_and_time_was_handed_over_as_changes_nothing()
    {
        // A reading is not an instant, whatever label the value carries. Marked as UTC or as local
        // it would otherwise be converted from the host's zone first, and the answer would depend
        // on where the server runs.
        var zone = Zone("Europe/Bucharest");
        var plain = TrackingCsvZones.Resolve(new DateTime(2026, 7, 12, 14, 5, 0, DateTimeKind.Unspecified), zone);
        TrackingCsvZones.Resolve(new DateTime(2026, 7, 12, 14, 5, 0, DateTimeKind.Utc), zone).ShouldBe(plain);
        TrackingCsvZones.Resolve(new DateTime(2026, 7, 12, 14, 5, 0, DateTimeKind.Local), zone).ShouldBe(plain);
    }
}
