// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Api.Common;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// The words a write-up's station list is printed in that are the document's own and not the
/// layout's.
/// </summary>
/// <remarks>
/// Written here for the same reason the journal's are: a layout asks for the list with one word
/// and the lines under it are composed in this application, so this is one of the few parts of a
/// write-up a club cannot word for itself and that therefore has to speak both languages by
/// itself. Both sets stand side by side, so a line added to one is a line missing from the other
/// at a glance.
/// </remarks>
public sealed record TripStationListWording
{
    /// <summary>What the list is, and what it must not be taken for.</summary>
    public required string Scope { get; init; }

    /// <summary>The name before the station every depth is measured below.</summary>
    public required string Datum { get; init; }

    /// <summary>What marks a station the survey calls an entrance.</summary>
    public required string Entrance { get; init; }

    /// <summary>
    /// The whole of what a reader who may not be told where the cave is gets in place of the
    /// list — said rather than left out, because a heading with nothing under it reads as a cave
    /// whose survey holds no stations.
    /// </summary>
    public required string NotShown { get; init; }

    /// <summary>
    /// The closing line of a list longer than the document prints, with the number printed and the
    /// number the survey holds.
    /// </summary>
    public required string Truncated { get; init; }

    public static TripStationListWording English { get; } = new()
    {
        Scope = "The stations of the survey this trip was followed in, with how far each lies "
            + "below the station depths are measured from. It is the survey's own list: it says "
            + "where the names in this document's journal are, and nothing about where anybody was.",
        Datum = "Depths measured below",
        Entrance = "entrance",
        NotShown = "The station list is not shown in this copy. A cave's stations are printed "
            + "only for a reader who may be told where that cave is.",
        Truncated = "Stations printed: {0}. Stations in the survey: {1}. The whole of it is in "
            + "the cave's survey, on the cave's own page.",
    };

    public static TripStationListWording Romanian { get; } = new()
    {
        Scope = "Stațiile topografiei în care a fost urmărită tura, fiecare cu adâncimea la care "
            + "se află sub stația de la care se măsoară adâncimile. Este lista topografiei: spune "
            + "unde se află numele din jurnalul acestui document, nu unde a fost cineva.",
        Datum = "Adâncimi măsurate sub",
        Entrance = "intrare",
        NotShown = "Lista stațiilor nu este inclusă în acest exemplar. Stațiile unei peșteri sunt "
            + "scrise doar pentru un cititor căruia i se poate spune unde se află peștera.",
        Truncated = "Stații tipărite: {0}. Stații în topografie: {1}. Toată lista se află în "
            + "topografia peșterii, pe pagina peșterii.",
    };

    /// <summary>
    /// The wording for a language, by its two-letter name. Anything this document has no words in
    /// — including nothing at all, which is a caller that is not a browser — is English, the
    /// language the rest of a write-up's own words are in.
    /// </summary>
    public static TripStationListWording For(string? language) =>
        string.Equals(language, "ro", StringComparison.OrdinalIgnoreCase) ? Romanian : English;
}

/// <summary>
/// A trip's station list, arranged as part of its write-up.
/// </summary>
/// <remarks>
/// <para>
/// This prints what it is handed and decides nothing about it. Whether this reader is told the
/// stations at all was settled where the list was read, under the rule the survey's own station
/// search is gated by; what arrives here is either the stations or the mark that says they were
/// withheld, and there is no branch that could print a station from a withheld list.
/// </para>
/// <para>
/// Nothing printed is measured from the present moment, so the same trip produces the same lines
/// tomorrow — which the copy kept on a trip relies on.
/// </para>
/// </remarks>
public static class TripReportStations
{
    /// <summary>
    /// Appends the station list's lines, or nothing when the trip has no list at all — so that a
    /// heading a layout wrote above the word is taken out with it. A list this reader may not be
    /// told is not nothing: it is one line saying so, and the heading above it stands.
    /// </summary>
    public static void Append(
        List<DocumentBlock> blocks, TripSurveyStationList? stations, TripStationListWording wording)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(wording);
        if (stations is null)
        {
            return;
        }

        if (!stations.Shown)
        {
            blocks.Add(DocumentBlock.Note(wording.NotShown));
            return;
        }

        // First, and not the layout's to leave out: a list of station names and depths under a
        // trip reads as where the party went, which is the one thing it is not.
        blocks.Add(DocumentBlock.Note(wording.Scope));

        if (stations.Datum is { } datum)
        {
            blocks.Add(DocumentBlock.Field(wording.Datum, datum));
        }

        foreach (var station in stations.Stations)
        {
            blocks.Add(DocumentBlock.Bullet(Line(
                station.Name,
                station.IsEntrance ? wording.Entrance : null,
                station.DepthM is { } metres
                    ? string.Create(CultureInfo.InvariantCulture, $"{metres:0.#} m")
                    : null)));
        }

        if (stations.Total > stations.Stations.Count)
        {
            blocks.Add(DocumentBlock.Note(string.Format(
                CultureInfo.InvariantCulture,
                wording.Truncated,
                stations.Stations.Count,
                stations.Total)));
        }
    }

    private static string Line(params string?[] pieces) =>
        string.Join(" · ", pieces.Where(piece => !string.IsNullOrWhiteSpace(piece)));
}
