// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Documents;

namespace SilexGis.Api.Features.TripLogs;

/// <summary>
/// The words a write-up's tracking journal is printed in that are the document's own and not
/// the layout's.
/// </summary>
/// <remarks>
/// <para>
/// Everything else a write-up says in fixed words a club can rewrite, because it stands in the
/// layout: a heading, the name before a value. This part cannot be reached that way — a layout
/// asks for the journal with one word and the lines under it are composed here — so it is the
/// one part of the document that has to speak two languages by itself. Both sets are written
/// side by side, so that a line added to one is a line missing from the other at a glance.
/// </para>
/// <para>
/// Which of them a document is written in is asked of whoever produced it, exactly as the layout
/// is: a language is a way of saying something and never a thing said, so it is no part of what
/// the copy kept on a trip keeps from its readers.
/// </para>
/// </remarks>
public sealed record TripJournalWording
{
    /// <summary>What the block is, and what it must not be taken for.</summary>
    public required string Scope { get; init; }

    public required string Status { get; init; }

    public required string Running { get; init; }

    public required string Closed { get; init; }

    public required string NeverStarted { get; init; }

    public required string StartedAt { get; init; }

    public required string ClosedAt { get; init; }

    /// <summary>Why some lines name no place, and that another copy may.</summary>
    public required string SomeWithheld { get; init; }

    public required string People { get; init; }

    public required string Reports { get; init; }

    public required string Underground { get; init; }

    public required string Out { get; init; }

    public required string Unheard { get; init; }

    public required string LastHeard { get; init; }

    public required string OffRoster { get; init; }

    /// <summary>The word that stands where a place was reported and is not printed.</summary>
    public required string Withheld { get; init; }

    /// <summary>Somebody this document is told no name for, by their number in the party.</summary>
    public required string Caver { get; init; }

    public required string Unnamed { get; init; }

    public required string Entered { get; init; }

    public required string AtStation { get; init; }

    public required string AtDepth { get; init; }

    public required string Note { get; init; }

    public required string Exited { get; init; }

    /// <summary>
    /// The closing line of a journal longer than the document prints, with the number printed
    /// and the number left out, naming where the rest is.
    /// </summary>
    public required string Truncated { get; init; }

    public static TripJournalWording English { get; } = new()
    {
        Scope = "A journal of what was reported to the surface while this trip was tracked. "
            + "It is not a callout record: nothing here watched a clock, and it raised no alarm.",
        Status = "Tracking",
        Running = "Running when this document was produced",
        Closed = "Closed",
        NeverStarted = "Never started — these reports were recorded without it",
        StartedAt = "First started",
        ClosedAt = "Tracking closed",
        SomeWithheld = "Some places are withheld. This document states a place only where the "
            + "reader it was produced for may be told it; somebody else's copy may say more.",
        People = "Each person, as last reported",
        Reports = "Reports, in time order",
        Underground = "Underground",
        Out = "Out",
        Unheard = "Not heard from",
        LastHeard = "last heard",
        OffRoster = "no longer on the roster",
        Withheld = "place withheld",
        Caver = "Caver {0}",
        Unnamed = "A person not named here",
        Entered = "Went in",
        AtStation = "At a station",
        AtDepth = "At a depth",
        Note = "Note",
        Exited = "Came out",
        Truncated = "Reports printed: {0}. Reports not printed: {1}. The whole log can be taken "
            + "out as a sheet from the trip's Tracking tab (Download the log (CSV)).",
    };

    public static TripJournalWording Romanian { get; } = new()
    {
        Scope = "Un jurnal a ceea ce s-a raportat la suprafață cât timp tura a fost urmărită. "
            + "Nu este o evidență a apelului de urgență: nimic de aici nu s-a uitat la ceas și nu a dat nicio alarmă.",
        Status = "Urmărire",
        Running = "Pornită când a fost produs acest document",
        Closed = "Încheiată",
        NeverStarted = "Nepornită — aceste rapoarte au fost înregistrate fără ea",
        StartedAt = "Începută prima dată",
        ClosedAt = "Urmărire încheiată",
        SomeWithheld = "Unele locuri sunt reținute. Documentul spune un loc doar acolo unde "
            + "cititorul pentru care a fost produs îl poate afla; exemplarul altcuiva poate spune mai mult.",
        People = "Fiecare persoană, după ultimul raport",
        Reports = "Rapoartele, în ordinea timpului",
        Underground = "În peșteră",
        Out = "Ieșit",
        Unheard = "Fără nicio veste",
        LastHeard = "ultima veste",
        OffRoster = "nu mai este pe listă",
        Withheld = "loc reținut",
        Caver = "Speolog {0}",
        Unnamed = "O persoană nenumită aici",
        Entered = "A intrat",
        AtStation = "La o stație",
        AtDepth = "La o adâncime",
        Note = "Notă",
        Exited = "A ieșit",
        Truncated = "Rapoarte tipărite: {0}. Rapoarte netipărite: {1}. Jurnalul întreg poate fi "
            + "scos ca foaie din fila Urmărire a turei (Descarcă jurnalul (CSV)).",
    };

    /// <summary>
    /// The wording for a language, by its two-letter name. Anything this document has no words in
    /// — including nothing at all, which is a caller that is not a browser — is English, the
    /// language the rest of a write-up's own words are in.
    /// </summary>
    public static TripJournalWording For(string? language) =>
        string.Equals(language, "ro", StringComparison.OrdinalIgnoreCase) ? Romanian : English;
}

/// <summary>
/// A trip's tracking journal, arranged as part of its write-up.
/// </summary>
/// <remarks>
/// <para>
/// This prints what it is handed and decides nothing about it. Which places the journal states
/// was settled where the journal was read, for the reader the document is built for; a report
/// whose place was kept back arrives marked and without a station or a depth, and what is
/// printed for it is its hour and a word. There is no branch here that could print a place for
/// one reader and not for another, because there is no reader here.
/// </para>
/// <para>
/// Every instant printed is one that was stored — when the watch started and closed, when a
/// report was about. Nothing is measured from the present moment, so the same trip produces the
/// same lines tomorrow, which the copy kept on a trip relies on.
/// </para>
/// </remarks>
public static class TripReportJournal
{
    /// <summary>
    /// The most reports one write-up prints.
    /// </summary>
    /// <remarks>
    /// A document somebody circulates, not the archive: a day underground is a few dozen
    /// reports, and a log brought in from a logger can be thousands. Past this the journal says
    /// how many it left out and where the whole of it is, rather than growing a document nobody
    /// can mail or stopping without a word.
    /// </remarks>
    public const int MaxReports = 500;

    private const string Moment = "yyyy-MM-dd HH:mm 'UTC'";

    /// <summary>
    /// Appends the journal's lines, or nothing when the trip has no journal — so that a heading
    /// a layout wrote above the word is taken out with it.
    /// </summary>
    public static void Append(
        List<DocumentBlock> blocks, TripTrackingJournal? journal, TripJournalWording wording)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(wording);
        if (journal is null)
        {
            return;
        }

        // First, and not the layout's to leave out: a list of hours and names under a trip reads
        // as the record of somebody having watched over the party, which is the one thing this
        // is not.
        blocks.Add(DocumentBlock.Note(wording.Scope));

        blocks.Add(DocumentBlock.Field(wording.Status, journal.State switch
        {
            TripTrackingState.Armed => wording.Running,
            TripTrackingState.Closed => wording.Closed,
            _ => wording.NeverStarted,
        }));
        if (journal.StartedAt is { } started)
        {
            blocks.Add(DocumentBlock.Field(wording.StartedAt, At(started)));
        }

        if (journal.ClosedAt is { } closed)
        {
            blocks.Add(DocumentBlock.Field(wording.ClosedAt, At(closed)));
        }

        // Said once and before the lines it explains, the way a list of caves states its own
        // shortfall: two people's copies of one trip differ here, and neither is a fault.
        if (journal.AnyWithheld)
        {
            blocks.Add(DocumentBlock.Note(wording.SomeWithheld));
        }

        if (journal.People.Count > 0)
        {
            blocks.Add(DocumentBlock.Subheading(wording.People));
            foreach (var person in journal.People)
            {
                blocks.Add(DocumentBlock.Bullet(Line(
                    Who(person.Name, person.Number, wording),
                    person.OnRoster ? null : wording.OffRoster,
                    person.Team,
                    person.Standing switch
                    {
                        TripStanding.Underground => wording.Underground,
                        TripStanding.Out => wording.Out,
                        _ => wording.Unheard,
                    },
                    person.LastHeardAt is { } heard ? $"{wording.LastHeard} {At(heard)}" : null,
                    Place(person.Station, person.DepthM, person.PlaceWithheld, wording) is { } place
                        ? person.PlaceAt is { } placed && placed != person.LastHeardAt
                            ? $"{place} ({At(placed)})"
                            : place
                        : null)));
            }
        }

        if (journal.Entries.Count > 0)
        {
            // Who a report is about is named the way the list above names them, number included,
            // so two people the document is told no name for are still two people in the log.
            var numbers = journal.People
                .GroupBy(person => person.CaverId)
                .ToDictionary(group => group.Key, group => group.First().Number);

            blocks.Add(DocumentBlock.Subheading(wording.Reports));
            foreach (var entry in journal.Entries.Take(MaxReports))
            {
                blocks.Add(DocumentBlock.Bullet(Line(
                    At(entry.At),
                    Who(entry.Name, numbers.GetValueOrDefault(entry.CaverId), wording),
                    entry.Team,
                    entry.Kind switch
                    {
                        TripPositionEventKind.Entered => wording.Entered,
                        TripPositionEventKind.AtStation => wording.AtStation,
                        TripPositionEventKind.AtDepth => wording.AtDepth,
                        TripPositionEventKind.Exited => wording.Exited,
                        _ => wording.Note,
                    },
                    Place(entry.Station, entry.DepthM, entry.Withheld, wording),
                    entry.Note)));
            }

            if (journal.Entries.Count > MaxReports)
            {
                blocks.Add(DocumentBlock.Note(string.Format(
                    CultureInfo.InvariantCulture,
                    wording.Truncated,
                    MaxReports,
                    journal.Entries.Count - MaxReports)));
            }
        }
    }

    /// <summary>
    /// Where, as this document may say it: the station and the depth it was handed, the word for
    /// a place kept back, or nothing for a report that claims no place.
    /// </summary>
    /// <remarks>
    /// The mark is read before the values, so a line that is marked prints the word whatever else
    /// it was handed. The journal never hands a marked line a place; this is the second of two
    /// locks rather than the only one.
    /// </remarks>
    private static string? Place(string? station, decimal? depthM, bool withheld, TripJournalWording wording)
    {
        if (withheld)
        {
            return wording.Withheld;
        }

        var depth = depthM is { } metres
            ? string.Create(CultureInfo.InvariantCulture, $"{metres:0.##} m")
            : null;
        return string.IsNullOrWhiteSpace(station) ? depth : depth is null ? station : $"{station}, {depth}";
    }

    private static string Who(string? name, int? number, TripJournalWording wording) =>
        !string.IsNullOrWhiteSpace(name) ? name
        : number is { } place ? string.Format(CultureInfo.InvariantCulture, wording.Caver, place)
        : wording.Unnamed;

    private static string Line(params string?[] pieces) =>
        string.Join(" · ", pieces.Where(piece => !string.IsNullOrWhiteSpace(piece)));

    /// <summary>
    /// An instant, in universal time and saying so — the way the write-up prints the moment a
    /// trip was published. A report's hour was given by whoever recorded it; printing it in any
    /// one reader's zone would make the same journal read differently from two copies.
    /// </summary>
    private static string At(DateTimeOffset moment) =>
        moment.UtcDateTime.ToString(Moment, CultureInfo.InvariantCulture);
}
