// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Api.Common;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Trips;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>One picture as it goes into a write-up: a rendering, and the line under it.</summary>
/// <param name="Image">
/// The bytes of a <em>rendering</em> of the photograph, never the stored upload. The upload
/// carries where it was taken; a rendering is drawn by this application and carries nothing.
/// </param>
/// <param name="Caption">What is written beneath it, or null for nothing.</param>
internal sealed record TripReportPlate(byte[] Image, string? Caption);

/// <summary>
/// A picture of a map, as it goes into one person's download of a write-up.
/// </summary>
/// <remarks>
/// The one part of a write-up this application did not draw. It was drawn by the browser of the
/// person the document is for, out of what the trip's page had been given for that person, and
/// it is carried here only for that person's own copy. Who that was and when travel with it,
/// because a picture forwarded without them reads as the trip's map rather than as one reader's.
/// </remarks>
/// <param name="Image">The picture, already redrawn by this application — never the upload.</param>
/// <param name="ShownTo">
/// How the person it was drawn for is named on every other surface, or null when nothing names
/// them. Never an address.
/// </param>
/// <param name="ShownOn">The day it was drawn, in universal time.</param>
internal sealed record TripReportMap(byte[] Image, string? ShownTo, DateOnly ShownOn);

/// <summary>
/// Everything a written-up trip is made of, already decided.
/// </summary>
/// <remarks>
/// Every field here was produced by the same request the trip page makes, and the names beside
/// the identifiers come from vocabularies every account may read. Nothing in this record is a
/// second answer to a question the trip read already answered: the caves are the list that read
/// returned — already stripped of every cave this reader may not open and every cave they may
/// not place, and carrying its own count of what was taken out — and the account of what went
/// wrong is present exactly when that read decided this caller may have it.
/// </remarks>
internal sealed record TripReportContent(
    TripLogDto Trip,
    string? TripTypeName,
    string? OrganizingGroupName,
    IReadOnlyDictionary<Guid, string> CaveNames,
    IReadOnlyDictionary<long, string> RoleNames,
    IReadOnlyDictionary<TripSectionKey, IReadOnlyDictionary<string, string>> SectionTitles,
    IReadOnlyList<TripReportPlate> Plates,
    // Present only in a copy somebody downloads for themselves, and only when they sent one.
    // Appended, and defaulted to nothing, so the copy filed against the trip — which is built
    // without ever naming this member — cannot come to carry one by an argument slipping along.
    TripReportMap? Map = null,
    // The trip's tracking journal as the reading this document is built from may be told it, read
    // only when the layout asks for it; null for a trip nobody followed, and for every layout that
    // does not print one. It arrives with its places already told or kept back.
    TripTrackingJournal? Tracking = null,
    // The language the document's own lines under that journal are written in, as two letters;
    // null is English, like the rest of a write-up's own words. Asked of whoever produced the
    // document, as its layout is, and deciding nothing about what the document says.
    string? Language = null);

/// <summary>
/// One trip, arranged as the document a club circulates, under the layout the club asked for.
/// </summary>
/// <remarks>
/// <para>
/// This turns what a caller was given into blocks and decides nothing else. It asks no question
/// about who may see what, because every such question was answered before the content reached
/// it — a second reading of the same rule on this side is how two surfaces come to disagree
/// about who may see what, months later and silently.
/// </para>
/// <para>
/// The layout only chooses what is written and in what order. It cannot reach past the answer
/// its producer was given: every name a layout may use is filled in from that answer alone, and
/// a name with nothing behind it — because the trip does not record it, or because this reader
/// is not given it — takes its whole line out rather than printing a label with a blank after it
/// or failing to produce the document at all.
/// </para>
/// </remarks>
internal static class TripReportDocument
{
    public static List<DocumentBlock> Blocks(
        TripReportContent content, IReadOnlyList<ReportTemplatePart> template)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(template);
        var trip = content.Trip;
        var blocks = new List<DocumentBlock>();
        var mapLine = content.Map is null ? null : MapLine(template);

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
                        // as the club's record of the trip.
                        if (trip.State == Domain.Entities.ActivityState.Draft)
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

                case ReportTemplateDirective.Roster:
                    AppendRoster(blocks, content);
                    break;

                case ReportTemplateDirective.Section:
                    blocks.AddRange(TripNarrativeComposition.Section(
                        NarrativeOf(trip), content.SectionTitles, TripNarrativeComposition.KeyOf(part.Text)));
                    break;

                case ReportTemplateDirective.Photographs:
                    AppendPlates(blocks, content);
                    break;

                case ReportTemplateDirective.Tracking:
                    TripReportJournal.Append(
                        blocks, content.Tracking, TripJournalWording.For(content.Language));
                    break;

                default:
                    break;
            }

            // After the line's own words, never instead of them: the written position is what a
            // reader can copy out of the document, and the picture is what shows where that is.
            if (content.Map is { } map && ReferenceEquals(part, mapLine))
            {
                AppendMap(blocks, map);
            }
        }

        // A layout that says nowhere where the trip went still carries the picture its reader
        // asked for, at the end. Leaving it out would be a download that silently lacks what the
        // page said it would hold, and nothing in the file would say why.
        if (content.Map is { } unplaced && mapLine is null)
        {
            AppendMap(blocks, unplaced);
        }

        return ReportComposition.Pruned(blocks);
    }

    /// <summary>
    /// The line of the layout a picture of a map goes after: the first that speaks of the trip's
    /// sketch, or failing that the first that speaks of where its party met.
    /// </summary>
    /// <remarks>
    /// Chosen by what the line asks for rather than by what it produced, so a trip that drew no
    /// sketch of its own still gets its picture where the layout talks about place — such a
    /// picture shows where the party met and the caves the trip names, which is most of what a
    /// club's older trips have to show. Null when the layout names neither.
    /// </remarks>
    private static ReportTemplatePart? MapLine(IReadOnlyList<ReportTemplatePart> template) =>
        template.FirstOrDefault(part => Names(part, "sketch"))
        ?? template.FirstOrDefault(part => Names(part, "meeting"));

    private static bool Names(ReportTemplatePart part, string name) =>
        ReportTemplateFormat.PlaceholdersIn(part.Text).Contains(name, StringComparer.Ordinal);

    /// <summary>
    /// The picture, with the line that says whose view it is.
    /// </summary>
    /// <remarks>
    /// The line is this document's own and no layout can take it off: it is written as the
    /// picture's caption, so wherever the picture goes the line goes with it. It is there because
    /// the picture is the one thing here that states a reading by showing it — two people's
    /// copies of the same trip may carry different maps, and each has to say which one it is.
    /// </remarks>
    private static void AppendMap(List<DocumentBlock> blocks, TripReportMap map)
    {
        var reader = string.IsNullOrWhiteSpace(map.ShownTo)
            ? "the person who produced this document"
            : map.ShownTo;
        blocks.Add(DocumentBlock.Picture(
            map.Image,
            string.Create(
                CultureInfo.InvariantCulture,
                $"Map as shown to {reader} on {map.ShownOn:yyyy-MM-dd} (UTC); positions as this "
                + $"reader may see them.")));
    }

    private static void AppendRoster(List<DocumentBlock> blocks, TripReportContent content)
    {
        foreach (var person in content.Trip.Proposers.Concat(content.Trip.Participants))
        {
            var role = content.RoleNames.GetValueOrDefault(person.RoleId);
            // Wall-clock strings, written to the minute and never through a date: they carry no
            // zone and must not be read as though they did.
            var times = person.EntryTime is null && person.ExitTime is null
                ? null
                : $"{Clock(person.EntryTime)} – {Clock(person.ExitTime)}";
            var line = new[] { person.Name, role, times, person.Note }
                .Where(x => !string.IsNullOrWhiteSpace(x));
            blocks.Add(DocumentBlock.Bullet(string.Join(" · ", line)));
        }
    }

    private static void AppendPlates(List<DocumentBlock> blocks, TripReportContent content)
    {
        if (content.Plates.Count == 0)
        {
            return;
        }

        // Two people producing the same write-up get two sets of pictures, because a gallery
        // answers with what its caller may read. The document says so rather than letting the
        // difference read as a fault in whoever produced it.
        blocks.Add(DocumentBlock.Note(
            "These are the photographs filed against this trip that the person who produced "
            + "this document may read; somebody else may be able to see more of them."));
        foreach (var plate in content.Plates)
        {
            blocks.Add(DocumentBlock.Picture(plate.Image, plate.Caption));
        }
    }

    /// <summary>
    /// The trip's own words as this reading was given them, in the shape the shared composition
    /// of a trip's form answers reads.
    /// </summary>
    /// <remarks>
    /// Taken off the trip's answer and decided nowhere here: whether the account of what went
    /// wrong is among them was settled when that answer was produced.
    /// </remarks>
    private static TripNarrative NarrativeOf(TripLogDto trip) => new(
        trip.Description, trip.Results, trip.FieldData, trip.Logistics, trip.Safety, trip.SafetySchemaVersion);

    /// <summary>
    /// What a line of the layout says once the trip has filled it in, or null when it says
    /// nothing and the line should not be written at all.
    /// </summary>
    /// <remarks>
    /// How a line behaves when one of its names comes back empty is a rule about the language
    /// rather than about trips, so it is applied from one place; what each name means is answered
    /// here, out of the reading this document's producer already has.
    /// </remarks>
    private static string? Fill(TripReportContent content, string text) =>
        ReportComposition.Fill(text, name => Resolve(content, name));

    private static string? Resolve(TripReportContent content, string name)
    {
        var trip = content.Trip;
        switch (name)
        {
            case "title": return trip.Title;
            case "purpose": return content.TripTypeName;
            case "dates": return Dates(trip);
            case "hours": return Hours(trip);
            case "club": return content.OrganizingGroupName;
            case "location": return trip.LocationText;
            case "caves": return Caves(content);
            case "weather": return trip.WeatherConditions;
            case "incident": return trip.HadIncident ? "Yes" : null;
            case "published":
                return trip.PublishedAt?.UtcDateTime.ToString(
                    "yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);
            case "description": return trip.Description;
            case "results": return trip.Results;
            case "depth": return trip.DepthReachedM is { } depth ? Metres(depth) : null;
            case "length": return trip.LengthSurveyedM is { } length ? Metres(length) : null;
            case "stations":
                return trip.SurveyStations?.ToString(CultureInfo.InvariantCulture);
            case "rope": return trip.RopeMetres is { } rope ? Metres(rope) : null;
            case "people": return People(trip);
            case "sketch": return Sketch(trip);
            case "meeting": return Meeting(trip);
            default: return TripNarrativeComposition.Answer(NarrativeOf(trip), name);
        }
    }

    private static string? Caves(TripReportContent content)
    {
        // Names, and never an identifier. The trip read has already taken out of its list every
        // cave this reader may not open and every cave they may not place; what is left resolves
        // to a name, and anything that did not would be printed as the identifier itself — which
        // is the one thing worth withholding, because it is enough to go and ask for the cave by
        // it. So a name that will not resolve takes its cave out of the line instead.
        //
        // What is missing is stated as a count, the way the pictures are: a circulated file that
        // simply listed fewer caves would read as a trip that went to fewer places, and the
        // difference between two people's copies would look like a fault in whoever produced one.
        var names = content.Trip.CaveIds
            .Select(id => content.CaveNames.GetValueOrDefault(id))
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .OrderBy(name => name, StringComparer.CurrentCulture)
            .ToList();
        var withheld = content.Trip.CavesWithheld;

        var shortfall = withheld == 0 ? null
            : withheld == 1 ? "1 cave not shown to you"
            : $"{withheld} caves not shown to you";

        if (names.Count == 0)
        {
            return shortfall;
        }

        return shortfall is null ? string.Join(", ", names) : $"{string.Join(", ", names)} (+{shortfall})";
    }

    /// <summary>
    /// How many people were underground — people, not rows: somebody who led the trip and
    /// surveyed it is two entries on the roster and one person.
    /// </summary>
    private static string? People(TripLogDto trip)
    {
        var people = trip.Proposers.Concat(trip.Participants).Select(x => x.CaverId).Distinct().Count();
        return people == 0 ? null : people == 1 ? "1 person" : $"{people} people";
    }

    /// <summary>
    /// The trip's own sketch, written down rather than drawn.
    /// </summary>
    /// <remarks>
    /// Nothing here draws a map, and this states no position the screen withheld: a trip's own
    /// geometry is exact for everybody who may read the trip, unlike a cave's, which is why it
    /// may be written out as it stands. The caves it names are still only the ones this reader
    /// may place, and none of their coordinates is ever resolved. What the shape discloses
    /// travels with the shape rather than beside it, so no layout can print the position and
    /// leave the warning out.
    /// </remarks>
    private static string? Sketch(TripLogDto trip)
    {
        if (trip.Geom?.ToGeometryOrNull() is not { IsEmpty: false } shape)
        {
            return null;
        }

        var centre = shape.Centroid;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{shape.GeometryType} of {shape.NumPoints} position(s), centred on "
            + $"{Math.Abs(centre.Y):F5}° {(centre.Y >= 0 ? "N" : "S")}, "
            + $"{Math.Abs(centre.X):F5}° {(centre.X >= 0 ? "E" : "W")}. Everyone who may read this "
            + $"trip sees this shape exactly as drawn, whatever protection the caves it names carry.");
    }

    /// <summary>
    /// Where the party gathers, written down rather than drawn.
    /// </summary>
    /// <remarks>
    /// Read out of the same reading the sketch above is, and carrying the same warning welded to
    /// the same value, because it is the same bargain: a meeting point is exact for everybody who
    /// may read the trip. It is the one that is worth saying twice — a meeting point stands where
    /// people actually park, which can be a few hundred metres from an entrance the reader of
    /// this very document was not told the trip names.
    /// </remarks>
    private static string? Meeting(TripLogDto trip)
    {
        if (trip.MeetingGeom?.ToGeometryOrNull() is not { IsEmpty: false } shape)
        {
            return null;
        }

        var centre = shape.Centroid;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{shape.GeometryType} of {shape.NumPoints} position(s), centred on "
            + $"{Math.Abs(centre.Y):F5}° {(centre.Y >= 0 ? "N" : "S")}, "
            + $"{Math.Abs(centre.X):F5}° {(centre.X >= 0 ? "E" : "W")}. Everyone who may read this "
            + $"trip sees this position exactly as placed, whatever protection the caves it names carry.");
    }

    private static string Dates(TripLogDto trip) =>
        trip.TripDateEnd is { } end && end != trip.TripDate
            ? $"{trip.TripDate:yyyy-MM-dd} – {end:yyyy-MM-dd}"
            : trip.TripDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string? Hours(TripLogDto trip) =>
        trip.EntryTime is null && trip.ExitTime is null
            ? null
            : $"{Clock(trip.EntryTime)} – {Clock(trip.ExitTime)}";

    private static string Clock(TimeOnly? time) =>
        time?.ToString("HH:mm", CultureInfo.InvariantCulture) ?? "—";

    private static string Metres(decimal value) =>
        string.Create(CultureInfo.InvariantCulture, $"{value:0.##} m");
}
