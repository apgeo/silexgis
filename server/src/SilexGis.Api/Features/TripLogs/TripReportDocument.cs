// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>One picture as it goes into a write-up: a rendering, and the line under it.</summary>
/// <param name="Image">
/// The bytes of a <em>rendering</em> of the photograph, never the stored upload. The upload
/// carries where it was taken; a rendering is drawn by this application and carries nothing.
/// </param>
/// <param name="Caption">What is written beneath it, or null for nothing.</param>
internal sealed record TripReportPlate(byte[] Image, string? Caption);

/// <summary>
/// Everything a written-up trip is made of, already decided.
/// </summary>
/// <remarks>
/// Every field here was produced by the same request the trip page makes, and the names beside
/// the identifiers come from vocabularies every account may read. Nothing in this record is a
/// second answer to a question the trip read already answered: the caves are the list that read
/// returned, which is the redacted one, and the account of what went wrong is present exactly
/// when that read decided this caller may have it.
/// </remarks>
internal sealed record TripReportContent(
    TripLogDto Trip,
    string? TripTypeName,
    string? OrganizingGroupName,
    IReadOnlyDictionary<Guid, string> CaveNames,
    IReadOnlyDictionary<long, string> RoleNames,
    IReadOnlyDictionary<TripSectionKey, IReadOnlyDictionary<string, string>> SectionTitles,
    IReadOnlyList<TripReportPlate> Plates);

/// <summary>Which of a trip's three sections a set of field titles belongs to.</summary>
internal enum TripSectionKey
{
    FieldData = 0,
    Logistics = 1,
    Safety = 2,
}

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
                    AppendSection(blocks, content, KeyOf(part.Text));
                    break;

                case ReportTemplateDirective.Photographs:
                    AppendPlates(blocks, content);
                    break;

                default:
                    break;
            }
        }

        return Pruned(blocks);
    }

    /// <summary>
    /// Takes out every heading nothing came out under.
    /// </summary>
    /// <remarks>
    /// A layout asks for a part before it can know whether the trip has anything to put in it, so
    /// an empty part is ordinary rather than a mistake — and a bare "Safety" heading on a
    /// circulated document reads as "nothing happened", which is a different statement from the
    /// one the record actually makes.
    /// </remarks>
    private static List<DocumentBlock> Pruned(List<DocumentBlock> blocks)
    {
        var kept = new List<DocumentBlock>(blocks.Count);
        for (var index = 0; index < blocks.Count; index++)
        {
            if (blocks[index].Kind != DocumentBlockKind.Heading)
            {
                kept.Add(blocks[index]);
                continue;
            }

            var next = index + 1;
            if (next < blocks.Count && blocks[next].Kind != DocumentBlockKind.Heading)
            {
                kept.Add(blocks[index]);
            }
        }

        return kept;
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
    /// One of the three per-purpose parts, written out under the names its purpose gave its
    /// questions.
    /// </summary>
    /// <remarks>
    /// The account of what went wrong arrives as nothing at all for a reader who may not change
    /// the trip — not as an empty bag — so nothing at all is what this writes.
    /// </remarks>
    private static void AppendSection(
        List<DocumentBlock> blocks, TripReportContent content, TripSectionKey key)
    {
        if (BagOf(content.Trip, key) is not { ValueKind: JsonValueKind.Object } written)
        {
            return;
        }

        var titles = content.SectionTitles.GetValueOrDefault(key);
        foreach (var property in written.EnumerateObject())
        {
            if (Value(property.Value) is not { } text)
            {
                continue;
            }

            blocks.Add(DocumentBlock.Field(titles?.GetValueOrDefault(property.Name) ?? property.Name, text));
        }
    }

    private static JsonElement? BagOf(TripLogDto trip, TripSectionKey key) => key switch
    {
        TripSectionKey.FieldData => trip.FieldData,
        TripSectionKey.Logistics => trip.Logistics,
        _ => trip.Safety,
    };

    private static TripSectionKey KeyOf(string section) => section switch
    {
        "logistics" => TripSectionKey.Logistics,
        "safety" => TripSectionKey.Safety,
        _ => TripSectionKey.FieldData,
    };

    /// <summary>
    /// What a line of the layout says once the trip has filled it in, or null when it says
    /// nothing and the line should not be written at all.
    /// </summary>
    /// <remarks>
    /// A name with nothing behind it takes the punctuation written next to it with it, so a line
    /// reading "{purpose} · {dates} · {club}" on a trip with no club comes out without a dangling
    /// separator. Only punctuation standing on its own between two names is touched; nothing
    /// alters the words a person wrote, and nothing collapses the line breaks inside prose.
    /// </remarks>
    private static string? Fill(TripReportContent content, string text)
    {
        var written = new StringBuilder();
        string? waiting = null;
        var anything = false;

        foreach (var token in ReportTemplateFormat.Tokens(text))
        {
            if (!token.IsPlaceholder)
            {
                waiting += token.Text;
                continue;
            }

            var value = Resolve(content, token.Text);
            if (string.IsNullOrWhiteSpace(value))
            {
                if (waiting is not null && !HasWords(waiting))
                {
                    waiting = null;
                }

                continue;
            }

            if (waiting is not null && (anything || HasWords(waiting)))
            {
                written.Append(waiting);
            }

            waiting = null;
            written.Append(value);
            anything = true;
        }

        if (waiting is not null && HasWords(waiting))
        {
            written.Append(waiting);
        }

        var filled = written.ToString().Trim();
        return HasWords(filled) ? filled : null;
    }

    private static bool HasWords(string text) => text.Any(char.IsLetterOrDigit);

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
            default: return Answer(content, name);
        }
    }

    /// <summary>
    /// One answer out of one part of the trip's form, by the name the form gave it.
    /// </summary>
    /// <remarks>
    /// Read out of the answer this caller was given and nowhere else, which is what makes a
    /// layout safe to let a club write: a part this reader is not given arrives as nothing, so
    /// naming one of its answers in a layout produces a document without that line rather than a
    /// way of asking for it.
    /// </remarks>
    private static string? Answer(TripReportContent content, string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot <= 0 || dot >= name.Length - 1)
        {
            return null;
        }

        var section = name[..dot];
        if (!ReportTemplateFormat.Sections.Contains(section, StringComparer.Ordinal))
        {
            return null;
        }

        return BagOf(content.Trip, KeyOf(section)) is { ValueKind: JsonValueKind.Object } bag
            && bag.TryGetProperty(name[(dot + 1)..], out var answer)
            ? Value(answer)
            : null;
    }

    private static string? Caves(TripReportContent content)
    {
        // The caves this trip names, as the trip read itself gave them: a cave whose position
        // this reader may not place is not on that list at all, and looking one up by any other
        // route is how it would come back.
        if (content.Trip.CaveIds.Count == 0)
        {
            return null;
        }

        var names = content.Trip.CaveIds
            .Select(id => content.CaveNames.GetValueOrDefault(id, id.ToString()))
            .OrderBy(name => name, StringComparer.CurrentCulture);
        return string.Join(", ", names);
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

    /// <summary>How one answer in a section reads, or null when it says nothing.</summary>
    private static string? Value(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "Yes",
        JsonValueKind.False => "No",
        JsonValueKind.Array => value.GetArrayLength() == 0
            ? null
            : string.Join(", ", value.EnumerateArray().Select(Value).Where(x => x is not null)),
        _ => null,
    };

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
