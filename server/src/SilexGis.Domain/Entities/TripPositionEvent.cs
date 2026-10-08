// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// What a tracking event says happened. Values are stored — append only, never renumber.
/// </summary>
public enum TripPositionEventKind : short
{
    /// <summary>The caver went underground.</summary>
    Entered = 0,

    /// <summary>The caver was reported at a named station of the trip's survey model.</summary>
    AtStation = 1,

    /// <summary>
    /// The caver was reported at a depth; the closest station at that depth was resolved
    /// server-side and stamped on the same row, beside the depth the reporter gave.
    /// </summary>
    AtDepth = 2,

    /// <summary>A note about the caver with no position claim.</summary>
    Note = 3,

    /// <summary>The caver is out of the cave.</summary>
    Exited = 4,

    /// <summary>
    /// Something said about the cave itself — loose rock above a pitch, water rising in a passage
    /// — and about nobody: the one kind whose row names no person. It may name a station, never
    /// a depth, and it always has words.
    /// </summary>
    /// <remarks>
    /// A kind of its own rather than a person's note with a place, because where a person is gets
    /// read off their latest report that names a place: a hazard written down as a note about
    /// whoever relayed it would move that person to the hazard on every surface that shows the
    /// party. A row with no person is one no fold over people can pick up.
    /// </remarks>
    CaveNote = 5,
}

/// <summary>
/// Where a position row came from. Values are stored — append only, never renumber.
/// </summary>
/// <remarks>
/// It says who <em>put the row here</em>, not what the row claims, and it is deliberately outside
/// everything that decides who may read a position. A relayed report and an imported scan are the
/// same kind of statement about where somebody was, guarded by the same anchor and withheld by the
/// same rule; a reader who may not learn a station must not be able to learn which of the two it
/// was either, because "this one came out of a phone" is a fact about the party's movements.
/// </remarks>
public enum TripPositionEventSource : short
{
    /// <summary>Somebody typed it in: word relayed out of the cave and entered by a coordinator.</summary>
    Reported = 0,

    /// <summary>
    /// Read out of a device export archive and confirmed point by point by a reviewer. The scan
    /// itself was made underground at a marked place; which station that place is was settled here.
    /// </summary>
    SpeleolocArchive = 1,
}

/// <summary>
/// Where the rows about people are told from the rows about nobody, for every query over reports.
/// </summary>
public static class TripPositionEventQueries
{
    /// <summary>
    /// The reports that are about somebody — every row but a note about the cave.
    /// </summary>
    /// <remarks>
    /// One spelling of the condition, so that "this read is about the party" is said the same way
    /// by every reader that folds, counts or publishes people's reports, and a new such reader has
    /// one thing to call rather than a predicate to remember. Asked of the person and not of the
    /// kind because the person is what those readers key on; the table ties the two together.
    /// </remarks>
    public static IQueryable<TripPositionEvent> AboutPeople(this IQueryable<TripPositionEvent> reports) =>
        reports.Where(e => e.CaverId != null);
}

/// <summary>
/// One report during a tracked trip, at the moment <see cref="RecordedAt"/> refers to — about one
/// caver, or, for a note about the cave, about nobody. A wrong report is corrected in place, keeping its row and its identity — anything
/// hung on that row survives the correction — and is taken off the log only when what it recorded
/// never happened rather than happened differently. Both acts land on the trip's audit timeline,
/// as recording does, and all three are refused on a watch that was never armed.
///
/// A position is a station reference — the station's name in the viewer's own spelling plus
/// the survey model it belongs to. No coordinate is ever stored here: geometry stays in
/// survey_stations, and what a caller may learn from the reference is decided at read time
/// by the location-protection rules, never at write time.
/// </summary>
public class TripPositionEvent : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid TripLogId { get; set; }

    /// <summary>
    /// The trip this row belongs to. Present so the model can hide the row while its trip is
    /// deleted; nothing reads the trip through it, and it is not loaded unless asked for.
    /// </summary>
    public TripLog TripLog { get; set; } = null!;

    /// <summary>
    /// Who the report is about. Null on exactly one kind,
    /// <see cref="TripPositionEventKind.CaveNote"/>, which is about the cave and nobody in it — the
    /// table refuses a person on that kind and the lack of one on any other.
    ///
    /// <para>
    /// <b>Everything that folds reports into people reads them through
    /// <see cref="TripPositionEventQueries.AboutPeople"/></b>, which leaves the rows about nobody
    /// out, and then takes the person with <see cref="Person"/>. A reader that does neither and
    /// groups by this column fails on the first note about the cave, or — worse — quietly counts a
    /// hazard as a party member's report.
    /// </para>
    /// </summary>
    public Guid? CaverId { get; set; }

    /// <summary>
    /// The person this report is about, for a reader that has already left out the rows about
    /// nobody. Asked of a note about the cave it throws rather than answering with an id that
    /// names no one: such a reader has a row it was never meant to see.
    /// </summary>
    public Guid Person() => CaverId ?? throw new InvalidOperationException(
        "A note about the cave names no person; it must be left out before reports are folded by person.");

    /// <summary>The caver's team at that moment; a team deleted later degrades to null.</summary>
    public Guid? TeamId { get; set; }

    public TripPositionEventKind Kind { get; set; }

    /// <summary>
    /// How this row came to exist. Provenance only: it never enters the decision about who may
    /// read the position, and it is never emitted beside a withheld one.
    /// </summary>
    public TripPositionEventSource Source { get; set; } = TripPositionEventSource.Reported;

    /// <summary>
    /// Survey model the station reference belongs to; null for placeless kinds.
    ///
    /// <para>
    /// <b>A bare id carrying no foreign key</b>, kept for the reason
    /// <see cref="ViewerStationName"/> is kept as text: a station path means whatever the model it
    /// was measured in says it means, so the report and the model it was made against are one
    /// statement and neither half survives usefully alone. A reference that blanked itself when the
    /// model row went would leave a station name beside no model at all — and that shape already
    /// means something else here, namely a position kept from this reader, which is the one
    /// reading under which a name recorded in an older survey gets drawn on the current one.
    /// </para>
    /// <para>
    /// So null on a row that names a station is impossible by construction, and a non-null id that
    /// resolves to nothing is the honest record of a report made against a survey somebody has
    /// since deleted. Protection is not evaluated against this — that is
    /// <see cref="CaveFeatureId"/>'s job, and a model id is not an anchor.
    /// </para>
    /// </summary>
    public Guid? SurveyModelId { get; set; }

    /// <summary>
    /// The model's cave, snapshotted at write time. This is the protection anchor: whether a
    /// reader may learn the station name is decided against this cave's chain, and the
    /// snapshot keeps that decidable after the model itself is replaced or deleted. A
    /// position row whose anchor is gone is withheld from everyone — fail closed, never open.
    /// </summary>
    public Guid? CaveFeatureId { get; set; }

    /// <summary>
    /// The station this report places the caver at, <b>in the survey viewer's own spelling</b> —
    /// which for one of the two line-plot formats is not the string the survey rows hold for the
    /// same station. Null for the kinds that claim no place.
    ///
    /// <para>
    /// Named for the spelling rather than for the field, because the spelling is the whole
    /// difference between a marker drawn on the model and one that silently never appears. A
    /// position exists to be shown at a place in the viewer, and a viewer resolves a station by its
    /// own name for it; a string in any other vocabulary is one the surface that has to draw it
    /// cannot look up, and it fails by drawing nothing rather than by complaining. Every write path
    /// converts on the way in (see <see cref="Surveys.SurveyStationNames"/>), which is also where
    /// the reason the two spellings differ at all is written down.
    /// </para>
    ///
    /// <para>
    /// Kept as text deliberately: history must stay readable after a model is replaced, and a name
    /// that no longer resolves is shown as unresolved rather than guessed.
    /// </para>
    /// </summary>
    public string? ViewerStationName { get; set; }

    /// <summary>
    /// The far end of a stretch, in the survey viewer's own spelling: the report says the person
    /// was somewhere between <see cref="ViewerStationName"/> and this station. Null on every report
    /// that names one station or none, which is nearly all of them.
    ///
    /// <para>
    /// A stretch is one statement about one survey: both ends are stations of the survey in
    /// <see cref="SurveyModelId"/>, resolved together when the report is written, and they are two
    /// different stations — "between A and A" is a report at A. It exists only on a station
    /// report; the table refuses any other shape, so a writer that changes a report's kind or its
    /// first station and forgets this column fails loudly instead of leaving half a stretch
    /// beside a place it never belonged to.
    /// </para>
    /// <para>
    /// It is place, exactly as the first station is: withheld from a reader who may not be told
    /// the cave's positions, kept out of the trip's history, and <b>told to signed-in readers
    /// only</b>. What a visitor without an account is given goes on carrying the first station and
    /// nothing else, so this column must not be read by anything that builds a published shape.
    /// </para>
    /// </summary>
    public string? ViewerToStationName { get; set; }

    /// <summary>The depth the reporter gave, metres positive down, for AtDepth events.</summary>
    public decimal? DepthEnteredM { get; set; }

    public string? Note { get; set; }

    /// <summary>
    /// The moment the report is about — caller-supplied, because word arrives out of the cave
    /// minutes or hours late. Never after the write itself.
    /// </summary>
    public DateTimeOffset RecordedAt { get; set; }

    public Guid? RecordedByUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// When the report was taken off the log; null while it is on it.
    ///
    /// <para>
    /// A report taken off is kept, not destroyed: it may be the only record of where somebody was,
    /// and nothing else holds a station once the row is gone — the trip's history deliberately
    /// keeps no place. So taking one off is this mark, putting it back clears the mark and returns
    /// the same row with everything hung on it, and destroying it is a second, separate act that
    /// only a marked row accepts.
    /// </para>
    /// <para>
    /// A marked row is hidden by the model itself, with the rows of a deleted trip, so no fold,
    /// list, count or published read has to remember to leave it out. The few readers that must
    /// see it — the list of removed reports, the acts on one, and whatever holds a person in
    /// place — ask past that on purpose.
    /// </para>
    /// </summary>
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>Who took the report off the log; null once that account is gone.</summary>
    public Guid? RemovedByUserId { get; set; }

    /// <summary>
    /// The name, in the model, of the key of the act of reporting that wrote this row — the value
    /// a sender mints once for one press of "record" and sends again with every re-send of it.
    /// Null on every row no keyed send wrote: imports, and reports sent without one.
    ///
    /// <para>
    /// <b>Deliberately a column the model knows and this class does not carry.</b> The key answers
    /// exactly one question — "has this act already been written?" — and is nobody's to read:
    /// whoever learns it can make a send of their own answer with somebody else's rows instead of
    /// being written. A member here would be copied into the trip's history by the snapshot taken
    /// of a new row, and would be one careless projection away from every answer built from a
    /// report. Kept in the model only, it cannot be emitted by anything that maps a report to what
    /// a caller reads; the write that stamps it and the lookup that asks for it name it through
    /// this constant.
    /// </para>
    /// <para>
    /// One act writes one row per person, so the key repeats across the rows of one act and is
    /// unique only together with the trip and the person. A note about the cave is one act and
    /// one row with no person, and "no person" counts there as a value like any other: the same
    /// key cannot hold two such rows on one trip.
    /// </para>
    /// </summary>
    public const string ClientKeyProperty = "ClientKey";

    public string AuditId => Id.ToString();

    public string? RootEntityType => nameof(TripLog);

    public string? RootEntityId => TripLogId.ToString();
}
