// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Features.Expeditions;

/// <summary>One picture as it goes into a camp's write-up: a rendering, and the line under it.</summary>
/// <param name="Image">
/// The bytes of a <em>rendering</em>, never the stored upload. The upload carries where it was
/// taken; a rendering is drawn by this application and carries nothing.
/// </param>
/// <param name="Caption">What is written beneath it, or null for nothing.</param>
internal sealed record ExpeditionReportPlate(byte[] Image, string? Caption);

/// <summary>
/// One member trip as the camp's write-up knows it: what the camp-level reading of trips returned
/// for it, and nothing looked up by any other route.
/// </summary>
/// <remarks>
/// A trip this caller may not read is not here at all — the reading that produced this list is the
/// same visibility walk the camp's own trip listing and its map apply, so a document can never
/// carry a trip the page would not show. No coordinate of any kind is on this record: a camp's
/// write-up names caves and never places them.
/// </remarks>
internal sealed record ExpeditionReportTrip(
    Guid Id,
    string Title,
    DateOnly Date,
    DateOnly? DateEnd,
    TimeOnly? EntryTime,
    TimeOnly? ExitTime,
    decimal? DepthReachedM,
    decimal? LengthSurveyedM,
    int? SurveyStations,
    decimal? RopeMetres,
    int People,
    IReadOnlyList<string> CaveNames);

/// <summary>One recorded stay at the camp, as the roster reading gave it.</summary>
internal sealed record ExpeditionReportStay(
    Guid CaverId, string CaverName, string? RoleName, DateOnly FromDate, DateOnly? ToDate, string? Note);

/// <summary>
/// Everything a written-up camp is made of, already decided.
/// </summary>
/// <remarks>
/// Every field here came out of a reading this caller already has — the camp's own read, the same
/// visibility walk over trips that its listing and its map use, and the roster reading with its two
/// rights. Nothing here is a second answer to a question one of those already answered, which is
/// what keeps a filed document from stating what the screen refuses to.
/// </remarks>
/// <param name="Roster">
/// Empty when the caller is not shown who was there. Empty and "nobody was recorded" are the same
/// thing on purpose: a write-up must not become a way of finding out that there is a roster.
/// </param>
/// <param name="TripPeople">
/// How many people the member trips come to, counted distinctly across all of them — not the sum
/// of their party sizes, which counts somebody on nine trips nine times, and not the largest single
/// party, which is the count of one afternoon. The camp's own roll-up counts it this way, and the
/// document must not state a different figure for the same reading.
/// </param>
internal sealed record ExpeditionReportContent(
    ExpeditionDto Camp,
    string? OrganizingGroupName,
    IReadOnlyList<ExpeditionReportTrip> Trips,
    int TripPeople,
    IReadOnlyList<ExpeditionReportStay> Roster,
    int RosterPeople,
    IReadOnlyList<ExpeditionReportPlate> Plates);

/// <summary>The member trips this reading may see, with the people they come to across all of them.</summary>
internal sealed record ExpeditionReportTrips(IReadOnlyList<ExpeditionReportTrip> Trips, int People);

/// <summary>
/// A camp, arranged as the document a club circulates, under the layout the club asked for.
/// </summary>
/// <remarks>
/// <para>
/// This turns what a caller was given into blocks and decides nothing else, exactly as the trip's
/// own write-up does: every question about who may see what was answered before the content reached
/// here, and answering one of them a second time on this side is how two surfaces come to disagree
/// months later and silently.
/// </para>
/// <para>
/// Every total is a sum over what this reading may see. That is stated on the document rather than
/// left to be discovered, because two people producing the same camp's write-up and getting
/// different figures is expected here — a camp is routinely readable by a wider audience than some
/// of the trips gathered into it — and an unexplained difference reads as a fault in whoever
/// produced it.
/// </para>
/// </remarks>
internal static class ExpeditionReportDocument
{
    public static List<DocumentBlock> Blocks(
        ExpeditionReportContent content, IReadOnlyList<ReportTemplatePart> template)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(template);
        var blocks = new List<DocumentBlock>();

        foreach (var part in template)
        {
            switch (part.Directive)
            {
                case ReportTemplateDirective.Title:
                    if (Fill(content, part.Text) is { } title)
                    {
                        blocks.Add(DocumentBlock.Title(title));

                        // Said on the document itself, whatever the layout says: a draft printed
                        // and handed round is exactly how an unfinished write-up comes to be read
                        // as the club's record of the camp.
                        if (content.Camp.State == Domain.Entities.ActivityState.Draft)
                        {
                            blocks.Add(DocumentBlock.Note("Draft — this write-up has not been published."));
                        }
                    }

                    break;

                case ReportTemplateDirective.Heading:
                    if (Fill(content, part.Text) is { } heading)
                    {
                        blocks.Add(DocumentBlock.Heading(heading));
                    }

                    break;

                case ReportTemplateDirective.Text:
                    if (Fill(content, part.Text) is { } prose)
                    {
                        blocks.Add(DocumentBlock.Paragraph(prose));
                    }

                    break;

                case ReportTemplateDirective.Note:
                    if (Fill(content, part.Text) is { } note)
                    {
                        blocks.Add(DocumentBlock.Note(note));
                    }

                    break;

                case ReportTemplateDirective.Bullet:
                    if (Fill(content, part.Text) is { } bullet)
                    {
                        blocks.Add(DocumentBlock.Bullet(bullet));
                    }

                    break;

                case ReportTemplateDirective.Field:
                    if (Fill(content, part.Text) is { } value)
                    {
                        blocks.Add(DocumentBlock.Field(part.Label ?? string.Empty, value));
                    }

                    break;

                case ReportTemplateDirective.Trips:
                    AppendTrips(blocks, content);
                    break;

                case ReportTemplateDirective.Days:
                    AppendDays(blocks, content);
                    break;

                case ReportTemplateDirective.Teams:
                    AppendTeams(blocks, content);
                    break;

                case ReportTemplateDirective.Roster:
                    AppendRoster(blocks, content);
                    break;

                case ReportTemplateDirective.Photographs:
                    AppendPlates(blocks, content);
                    break;

                default:
                    break;
            }
        }

        return ReportComposition.Pruned(blocks);
    }

    /// <summary>The trips this reading may see, one line each.</summary>
    private static void AppendTrips(List<DocumentBlock> blocks, ExpeditionReportContent content)
    {
        if (content.Trips.Count == 0)
        {
            return;
        }

        // Said once, here, rather than left for a reader to infer from a number that looks low:
        // a camp is routinely readable by more people than the trips inside it are.
        blocks.Add(DocumentBlock.Note(
            "These are the trips gathered into this camp that the person who produced this "
            + "document may read; somebody else may be able to see more of them."));

        foreach (var trip in content.Trips.OrderBy(t => t.Date).ThenBy(t => t.Title, StringComparer.Ordinal))
        {
            var line = new[]
            {
                Dates(trip.Date, trip.DateEnd),
                trip.Title,
                trip.CaveNames.Count == 0 ? null : string.Join(", ", trip.CaveNames),
                trip.People == 0 ? null : People(trip.People),
                Hours(trip.EntryTime, trip.ExitTime),
            }.Where(x => !string.IsNullOrWhiteSpace(x));
            blocks.Add(DocumentBlock.Bullet(string.Join(" · ", line)));
        }
    }

    /// <summary>
    /// The camp day by day: which of the trips this reading may see ran on each day.
    /// </summary>
    /// <remarks>
    /// A day nothing ran on is left out rather than printed empty. A fortnight has rest days and
    /// travel days, and a document listing "12 August — nothing" fourteen times says less than one
    /// that lists the days something happened. A trip spanning two days appears under each.
    /// </remarks>
    private static void AppendDays(List<DocumentBlock> blocks, ExpeditionReportContent content)
    {
        var byDay = new SortedDictionary<DateOnly, List<ExpeditionReportTrip>>();
        foreach (var trip in content.Trips)
        {
            var last = trip.DateEnd is { } end && end > trip.Date ? end : trip.Date;
            for (var day = trip.Date; day <= last; day = day.AddDays(1))
            {
                if (!byDay.TryGetValue(day, out var running))
                {
                    running = [];
                    byDay[day] = running;
                }

                running.Add(trip);
            }
        }

        foreach (var (day, running) in byDay)
        {
            var titles = running
                .OrderBy(t => t.Title, StringComparer.Ordinal)
                .Select(t => t.Title);
            blocks.Add(DocumentBlock.Field(
                day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), string.Join("; ", titles)));
        }
    }

    /// <summary>
    /// Who was there in each role — the part of a camp that has no trip analogue at all.
    /// </summary>
    /// <remarks>
    /// Counted distinctly by person within each role: the roster holds one row per person per role
    /// per stay, so somebody who cooked for two separate stretches is one cook.
    /// </remarks>
    private static void AppendTeams(List<DocumentBlock> blocks, ExpeditionReportContent content)
    {
        var teams = content.Roster
            .Where(x => !string.IsNullOrWhiteSpace(x.RoleName))
            .GroupBy(x => x.RoleName!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.CurrentCulture);

        foreach (var team in teams)
        {
            var names = team
                .Select(x => x.CaverName)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(x => x, StringComparer.CurrentCulture);
            blocks.Add(DocumentBlock.Field(team.Key, string.Join(", ", names)));
        }
    }

    private static void AppendRoster(List<DocumentBlock> blocks, ExpeditionReportContent content)
    {
        foreach (var stay in content.Roster)
        {
            var line = new[]
            {
                stay.CaverName,
                stay.RoleName,
                Dates(stay.FromDate, stay.ToDate),
                stay.Note,
            }.Where(x => !string.IsNullOrWhiteSpace(x));
            blocks.Add(DocumentBlock.Bullet(string.Join(" · ", line)));
        }
    }

    private static void AppendPlates(List<DocumentBlock> blocks, ExpeditionReportContent content)
    {
        if (content.Plates.Count == 0)
        {
            return;
        }

        blocks.Add(DocumentBlock.Note(
            "These are the photographs filed against this camp's trips that the person who "
            + "produced this document may read; somebody else may be able to see more of them."));
        foreach (var plate in content.Plates)
        {
            blocks.Add(DocumentBlock.Picture(plate.Image, plate.Caption));
        }
    }

    private static string? Fill(ExpeditionReportContent content, string text) =>
        ReportComposition.Fill(text, name => Resolve(content, name));

    private static string? Resolve(ExpeditionReportContent content, string name)
    {
        var camp = content.Camp;
        switch (name)
        {
            case "title": return camp.Name;
            case "description": return camp.Description;
            case "dates": return Dates(camp.StartDate, camp.EndDate);
            case "days": return Days(camp);
            case "club": return content.OrganizingGroupName;
            case "area": return Area(content);
            case "trips": return content.Trips.Count == 0
                ? null
                : content.Trips.Count == 1 ? "1 trip" : $"{content.Trips.Count} trips";
            case "caves": return Caves(content);
            case "people": return PeopleCount(content);
            case "hours": return UndergroundHours(content);
            case "depth": return Metres(content.Trips.Max(t => t.DepthReachedM));
            case "length": return Metres(Sum(content.Trips.Select(t => t.LengthSurveyedM)));
            case "stations":
                var stations = content.Trips.Sum(t => t.SurveyStations ?? 0);
                return stations == 0 ? null : stations.ToString(CultureInfo.InvariantCulture);
            case "rope": return Metres(Sum(content.Trips.Select(t => t.RopeMetres)));
            case "published":
                return camp.PublishedAt?.UtcDateTime.ToString(
                    "yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
            default: return null;
        }
    }

    /// <summary>
    /// How many people the camp comes to.
    /// </summary>
    /// <remarks>
    /// The roster where this caller is shown it, and otherwise the people on the trips they may
    /// read — never the two added together, and never a count of rows: a roster holds one row per
    /// person per role per stay, and a trip holds one per person per role, so counting rows counts
    /// a cook who also surveyed twice. Both figures are already distinct by person where they were
    /// worked out, which is the only place that can do it.
    /// </remarks>
    private static string? PeopleCount(ExpeditionReportContent content)
    {
        var people = content.RosterPeople > 0 ? content.RosterPeople : content.TripPeople;
        return people == 0 ? null : People(people);
    }

    private static string People(int people) =>
        people == 1 ? "1 person" : $"{people} people";

    private static string? Caves(ExpeditionReportContent content)
    {
        // Named, never placed, and only the caves that came back from the trips this reading may
        // read: nothing here resolves a coordinate for any of them, so the question of who may be
        // told where a cave is does not arise on this document at all.
        var names = content.Trips
            .SelectMany(t => t.CaveNames)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.CurrentCulture)
            .ToList();
        return names.Count == 0 ? null : string.Join(", ", names);
    }

    private static string? Days(ExpeditionDto camp)
    {
        var last = camp.EndDate is { } end && end > camp.StartDate ? end : camp.StartDate;
        var days = last.DayNumber - camp.StartDate.DayNumber + 1;
        return days == 1 ? "1 day" : $"{days} days";
    }

    /// <summary>
    /// The camp's own working area, written down rather than drawn.
    /// </summary>
    /// <remarks>
    /// A camp's own geometry is the area somebody drew on the plan — not a position derived from
    /// the caves its trips reached — and it is exact for everybody who may read the camp, the same
    /// way a trip's own sketch is. What the shape discloses travels with the shape rather than
    /// beside it, so no layout can print the position and leave the sentence out.
    /// </remarks>
    private static string? Area(ExpeditionReportContent content)
    {
        if (content.Camp.Geom?.ToGeometryOrNull() is not { IsEmpty: false } shape)
        {
            return null;
        }

        var centre = shape.Centroid;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{shape.GeometryType} of {shape.NumPoints} position(s), centred on "
            + $"{Math.Abs(centre.Y):F5}° {(centre.Y >= 0 ? "N" : "S")}, "
            + $"{Math.Abs(centre.X):F5}° {(centre.X >= 0 ? "E" : "W")}. This is the area the camp "
            + $"worked in, drawn on its own plan; everyone who may read this camp sees it exactly "
            + $"as drawn, and it says nothing about where the caves it names are.");
    }

    /// <summary>
    /// How long the camp's trips came to underground.
    /// </summary>
    /// <remarks>
    /// Worked out by the one rule that knows what the two clock times mean — an exit earlier than
    /// the entry is the next morning on a single-day trip, and a trip that says which day it ended
    /// has its whole days added. Re-deriving it here would drop both cases: a fortnight of night
    /// pushes would come to nothing at all, and a thirty-hour push would print as six hours.
    ///
    /// Summed in minutes and divided once at the end, so no trip's figure is rounded before it is
    /// added. A trip whose times cannot say contributes nothing rather than a zero.
    /// </remarks>
    private static string? UndergroundHours(ExpeditionReportContent content)
    {
        var minutes = content.Trips
            .Select(t => TripDuration.UndergroundMinutes(t.Date, t.DateEnd, t.EntryTime, t.ExitTime))
            .Where(x => x is not null)
            .Sum(x => x!.Value);
        return minutes <= 0
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60d:0.#} h");
    }

    private static decimal? Sum(IEnumerable<decimal?> values)
    {
        var present = values.Where(x => x is not null).Select(x => x!.Value).ToList();
        return present.Count == 0 ? null : present.Sum();
    }

    private static string? Metres(decimal? value) =>
        value is null ? null : string.Create(CultureInfo.InvariantCulture, $"{value:0.##} m");

    private static string Dates(DateOnly start, DateOnly? end) =>
        end is { } last && last != start
            ? $"{start:yyyy-MM-dd} – {last:yyyy-MM-dd}"
            : start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Hours(TimeOnly? entry, TimeOnly? exit) =>
        entry is null && exit is null
            ? null
            : $"{Clock(entry)} – {Clock(exit)}";

    private static string Clock(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";
}
