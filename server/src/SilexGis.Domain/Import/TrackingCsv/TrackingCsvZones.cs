// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace SilexGis.Domain.Import.TrackingCsv;

/// <summary>How often a zone's clocks showed a given date and time.</summary>
public enum TrackingCsvWallClockKind
{
    /// <summary>Once, which is every time of every ordinary day.</summary>
    Once,

    /// <summary>
    /// Twice: the clocks were put back over it, so the same reading names two instants an hour
    /// apart.
    /// </summary>
    Twice,

    /// <summary>Never: the clocks were put forward over it, so no clock in the zone showed it.</summary>
    Never,

    /// <summary>
    /// Off the calendar: the reading is within a day of the first or the last date there is, and
    /// the instant it names in this zone falls outside what a date can hold.
    /// </summary>
    OffTheCalendar,
}

/// <summary>A date and time on a zone's clocks, brought to an instant.</summary>
/// <param name="At">
/// The instant, in UTC. For <see cref="TrackingCsvWallClockKind.Twice"/> the earlier of the two;
/// for <see cref="TrackingCsvWallClockKind.Never"/> and
/// <see cref="TrackingCsvWallClockKind.OffTheCalendar"/> there is none and this is the default value.
/// </param>
public readonly record struct TrackingCsvWallClock(TrackingCsvWallClockKind Kind, DateTimeOffset At);

/// <summary>
/// The zone a sheet's times were written in: which names are accepted for one, and what a date and
/// time on that zone's clocks is as an instant.
/// </summary>
/// <remarks>
/// <para>
/// A club's sheet says "14:05" and means the clock on the wall of the hut. The log stores instants,
/// so somebody has to say whose wall — and it cannot be the server's, because the same sheet would
/// then import differently after the installation moved to another host. The importer names the
/// zone; this is the one place that turns the name into a zone and a reading into an instant.
/// </para>
/// <para>
/// <b>A zone is an IANA name, and only one the host's zone database carries.</b> The name is
/// "Area/Location" as the database and every browser spell it ("Europe/Bucharest"), or "UTC".
/// The framework resolves such a name on Linux from the system's zone files and on Windows by
/// mapping it onto the operating system's own zones, so one name means one zone on either. The
/// shape is checked here, before the framework is asked, because the framework is more generous
/// than that and differently generous per platform — and three of its generosities are wrong for
/// a record:
/// <list type="bullet">
/// <item>On Linux the lookup is a file under the zone directory, and that directory also holds
/// <c>localtime</c> — the host's own zone, the one reading this whole option exists to keep out —
/// beside other files that are not places at all. None of them has an area, so requiring one
/// refuses them all.</item>
/// <item>Linux also ships a second copy of every zone under <c>right/</c>, which counts leap
/// seconds and so answers instants some tens of seconds away from civil time, and on some hosts a
/// third under <c>posix/</c>. Windows has neither. Both prefixes are refused by name.</item>
/// <item>A Windows zone name ("GTB Standard Time") resolves on both platforms but is not what a
/// browser offers and not stable across them; its spaces, or its lack of an area, refuse it.</item>
/// </list>
/// A name that passes the shape and that this host does not carry is simply not found. It is never
/// replaced by UTC or by the host's zone: a zone quietly swapped for another is exactly the
/// three-hour error the option is there to remove, arriving again without a word.
/// </para>
/// <para>
/// <b>A place the database has renamed is the same zone under either spelling.</b> The database
/// respells a place now and then — Kiev became Kyiv, Calcutta became Kolkata — and keeps the old
/// spelling as a link in a separate "backward" list. Whether a host carries that list is a matter
/// of packaging: recent Debian and Ubuntu moved it into an optional package, while Windows and
/// older hosts resolve the old spellings and may not yet know the new ones. And the browsers
/// disagree with one another: one engine family still reports and lists the old spelling for a
/// machine in India, Ukraine, Vietnam, Nepal, Myanmar or Argentina and does not list the new one
/// at all, while another does the reverse. Left to the host, the chooser would offer such an
/// importer a name the server refuses and no other name for the same clock. So the renames those
/// engines still emit are held here (<see cref="Respelled"/>), and a name this host does not carry
/// is tried once more under its other spelling. That is not the substitution refused above: both
/// spellings are, by the database's own definition, one zone with one set of rules, so no instant
/// moves.
/// </para>
/// </remarks>
public static class TrackingCsvZones
{
    /// <summary>The longest name accepted. The longest in the database is under forty characters.</summary>
    public const int MaxNameLength = 64;

    private static readonly Regex AreaAndLocation = new(
        "^[A-Za-z][A-Za-z0-9_+-]*(/[A-Za-z0-9_+-]+){1,2}$",
        RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture);

    /// <summary>
    /// Places the zone database has respelled, old spelling first, limited to the ones a current
    /// browser engine still reports or lists under the old spelling. Each pair is one zone.
    /// </summary>
    /// <remarks>
    /// A fixed list and not the database's whole "backward" file on purpose: most of that file is
    /// links between <em>different</em> places that merely share rules today, which is a judgement
    /// about clocks and not a spelling, and none of those is a name a browser hands over.
    /// </remarks>
    private static readonly (string Old, string Current)[] Respelled =
    [
        ("Africa/Asmera", "Africa/Asmara"),
        ("America/Buenos_Aires", "America/Argentina/Buenos_Aires"),
        ("America/Catamarca", "America/Argentina/Catamarca"),
        ("America/Cordoba", "America/Argentina/Cordoba"),
        ("America/Godthab", "America/Nuuk"),
        ("America/Indianapolis", "America/Indiana/Indianapolis"),
        ("America/Jujuy", "America/Argentina/Jujuy"),
        ("America/Louisville", "America/Kentucky/Louisville"),
        ("America/Mendoza", "America/Argentina/Mendoza"),
        ("Asia/Calcutta", "Asia/Kolkata"),
        ("Asia/Katmandu", "Asia/Kathmandu"),
        ("Asia/Rangoon", "Asia/Yangon"),
        ("Asia/Saigon", "Asia/Ho_Chi_Minh"),
        ("Atlantic/Faeroe", "Atlantic/Faroe"),
        ("Europe/Kiev", "Europe/Kyiv"),
        ("Pacific/Enderbury", "Pacific/Kanton"),
        ("Pacific/Ponape", "Pacific/Pohnpei"),
        ("Pacific/Truk", "Pacific/Chuuk"),
    ];

    /// <summary>The old spellings of <see cref="Respelled"/>, for a caller that has to cover them all.</summary>
    public static IReadOnlyList<(string Old, string Current)> RespelledPlaces => Respelled;

    /// <summary>
    /// The zone a name stands for, or false when the name is not one a sheet may be read in — not
    /// shaped as an IANA name, or not carried by this host under either of its spellings.
    /// </summary>
    /// <remarks>
    /// The zone answers to the spelling this host carries, which for a respelled place may not be
    /// the one asked for; a caller that shows the name back shows the one it was given.
    /// </remarks>
    public static bool TryFind(string? name, [NotNullWhen(true)] out TimeZoneInfo? zone)
    {
        zone = null;
        if (name is null || name.Length > MaxNameLength)
        {
            return false;
        }

        if (string.Equals(name, "UTC", StringComparison.OrdinalIgnoreCase))
        {
            zone = TimeZoneInfo.Utc;
            return true;
        }

        if (!AreaAndLocation.IsMatch(name)
            || name.StartsWith("right/", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("posix/", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(name, out zone))
        {
            return true;
        }

        // Compared exactly as written: the zone files are case-sensitive, and a name that only
        // resembles a respelled one is not one.
        foreach (var (old, current) in Respelled)
        {
            var other = string.Equals(name, old, StringComparison.Ordinal) ? current
                : string.Equals(name, current, StringComparison.Ordinal) ? old
                : null;
            if (other is not null)
            {
                return TimeZoneInfo.TryFindSystemTimeZoneById(other, out zone);
            }
        }

        return false;
    }

    /// <summary>
    /// What a date and time on a zone's clocks is as an instant.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every reading but two hours a year names exactly one instant, at the offset the zone kept on
    /// that date — which is why this is asked per reading and never once for a file: a sheet that
    /// runs from summer into winter has two offsets in it.
    /// </para>
    /// <para>
    /// <b>A reading the clocks skipped has no instant, and is not given one.</b> When the clocks go
    /// forward, an hour of readings is never shown by any clock in the zone. A cell that carries
    /// one was mistyped, or the sheet was not kept on this zone's clocks, and either way the
    /// instant is unknown. Pushing it an hour on, as a browser does for a typed date, would file a
    /// report at a moment nobody wrote — and a browser cannot ask its user a question, where a
    /// sheet under review can be asked for the cell to be corrected. So the answer is
    /// <see cref="TrackingCsvWallClockKind.Never"/>, and the caller refuses the row.
    /// </para>
    /// <para>
    /// <b>A reading the clocks showed twice is taken as the first time they showed it, and said to
    /// be.</b> When the clocks go back, an hour of readings happens twice, an hour apart, and the
    /// reading alone cannot say which. The report is real either way, so it is not refused:
    /// keeping a true report out of the log costs more than placing it at most one hour out, on
    /// one night a year, with the row marked. The first passage is chosen, and not the second, for
    /// one reason that can be checked: it is what a browser answers for the same date and time
    /// typed into a form, so a row of the sheet and a report typed for the same clock reading land
    /// on the same instant and the sheet corrects that report instead of standing beside it. The
    /// answer is <see cref="TrackingCsvWallClockKind.Twice"/> so the caller can warn; a cell that
    /// writes its offset settles the question outright and never reaches this method.
    /// </para>
    /// <para>
    /// The first passage is the one at the larger offset — an instant is the reading minus the
    /// offset — which holds whichever way a zone names its two seasons, including the few that
    /// call winter the shifted one.
    /// </para>
    /// <para>
    /// <b>A reading at the very edge of the calendar may have no instant a date can hold.</b> The
    /// first and the last day there are can be written in a cell, and east or west of Greenwich the
    /// instant then falls before the first or after the last. That is answered as
    /// <see cref="TrackingCsvWallClockKind.OffTheCalendar"/> and never thrown: one cell with a
    /// mistyped year must cost its own row and not the reading of the whole sheet.
    /// </para>
    /// </remarks>
    /// <param name="wall">The date and time as written. Its kind is ignored: it is a reading, not an instant.</param>
    /// <param name="zone">The zone whose clocks showed it.</param>
    public static TrackingCsvWallClock Resolve(DateTime wall, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var reading = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);

        if (zone.IsInvalidTime(reading))
        {
            return new TrackingCsvWallClock(TrackingCsvWallClockKind.Never, default);
        }

        var kind = TrackingCsvWallClockKind.Once;
        TimeSpan offset;
        if (zone.IsAmbiguousTime(reading))
        {
            kind = TrackingCsvWallClockKind.Twice;
            offset = zone.GetAmbiguousTimeOffsets(reading).Max();
        }
        else
        {
            offset = zone.GetUtcOffset(reading);
        }

        return TryInstant(reading, offset, out var at)
            ? new TrackingCsvWallClock(kind, at)
            : new TrackingCsvWallClock(TrackingCsvWallClockKind.OffTheCalendar, default);
    }

    /// <summary>
    /// The instant a reading at a given offset names, in UTC, or false when that instant is before
    /// the first or after the last a date can hold.
    /// </summary>
    /// <remarks>
    /// Asked before the instant is built because building it throws for exactly these readings,
    /// and a throw from one cell is the whole request failing.
    /// </remarks>
    internal static bool TryInstant(DateTime reading, TimeSpan offset, out DateTimeOffset at)
    {
        var ticks = reading.Ticks - offset.Ticks;
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
        {
            at = default;
            return false;
        }

        at = new DateTimeOffset(DateTime.SpecifyKind(reading, DateTimeKind.Unspecified), offset).ToUniversalTime();
        return true;
    }
}
