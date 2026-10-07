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
/// One report about one caver during a tracked trip, at the moment <see cref="RecordedAt"/>
/// refers to. A wrong report is corrected in place, keeping its row and its identity — anything
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

    public Guid CaverId { get; set; }

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

    public string AuditId => Id.ToString();

    public string? RootEntityType => nameof(TripLog);

    public string? RootEntityId => TripLogId.ToString();
}
