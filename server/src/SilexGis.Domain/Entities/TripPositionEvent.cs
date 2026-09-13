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
/// One report about one caver during a tracked trip, at the moment <see cref="RecordedAt"/>
/// refers to. The log is append-only: a wrong report is deleted and re-entered, never edited,
/// so both directions land on the trip's audit timeline.
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

    public Guid CaverId { get; set; }

    /// <summary>The caver's team at that moment; a team deleted later degrades to null.</summary>
    public Guid? TeamId { get; set; }

    public TripPositionEventKind Kind { get; set; }

    /// <summary>Survey model the station reference belongs to; null for placeless kinds.</summary>
    public Guid? SurveyModelId { get; set; }

    /// <summary>
    /// The model's cave, snapshotted at write time. This is the protection anchor: whether a
    /// reader may learn the station name is decided against this cave's chain, and the
    /// snapshot keeps that decidable after the model itself is replaced or deleted. A
    /// position row whose anchor is gone is withheld from everyone — fail closed, never open.
    /// </summary>
    public Guid? CaveFeatureId { get; set; }

    /// <summary>
    /// Station name in the viewer's spelling. Kept as text deliberately: history must stay
    /// readable after a model is replaced, and a name that no longer resolves is shown as
    /// unresolved rather than guessed.
    /// </summary>
    public string? StationName { get; set; }

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

    public string AuditId => Id.ToString();

    public string? RootEntityType => nameof(TripLog);

    public string? RootEntityId => TripLogId.ToString();
}
