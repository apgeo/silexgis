// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Whether a trip's live tracking is running. Deliberately separate from the trip's own
/// lifecycle state: arming tracking is an operational act during the trip, not a step of
/// planning or writing it up. Values are stored — append only, never renumber.
/// </summary>
public enum TripTrackingState : short
{
    Off = 0,
    Armed = 1,
    Closed = 2,
}

/// <summary>
/// Per-trip live-tracking configuration, one row per tracked trip. Holds the choices the
/// depth resolver and the viewer need: which survey model the trip is tracked against,
/// which station is the depth datum, and which parts of the survey depth matching may
/// consider (parallel shafts share depths; the filter names the parts the party is in).
/// </summary>
public class TripTracking : ITimestamped, IAuditable, IAuditChild
{
    public Guid TripLogId { get; set; }

    public TripTrackingState State { get; set; }

    /// <summary>
    /// The survey model cavers are placed in; chosen per trip by the admin.
    ///
    /// <para>
    /// <b>A bare id, deliberately carrying no foreign key</b> — the same stance
    /// <see cref="TripPositionEvent.ViewerStationName"/> takes, and for the same reason. A
    /// reference that blanks itself when the model row goes turns a watch armed on a model
    /// somebody deleted into a watch that reads as though it never had one, which is
    /// indistinguishable from the shape a reader who may not be told the model sees. Keeping the
    /// id makes "the model this watch is armed on is no longer here" a state the read can state
    /// out loud instead of a null two other things already mean.
    /// </para>
    /// <para>
    /// Nothing ever re-points this on a watch's behalf. A model arriving for the cave is a new
    /// model, never a replacement of this one, and moving a live watch onto it silently is the
    /// failure this field is arranged to prevent: the party would keep being drawn, on stations
    /// that are not the ones their reports named. Re-pointing is an administrator's deliberate
    /// act, and what it costs is said on the surface where it is taken.
    /// </para>
    /// </summary>
    public Guid? SurveyModelId { get; set; }

    /// <summary>
    /// The model's cave, snapshotted when the model is chosen — the protection anchor for
    /// the config's own station vocabulary (reference station, depth filter), evaluable
    /// after the model is gone. Null with station names present reads as withheld.
    /// </summary>
    public Guid? CaveFeatureId { get; set; }

    /// <summary>
    /// Station whose altitude is the depth datum, in the survey viewer's spelling; null means the
    /// model's highest entrance-flagged station.
    ///
    /// <para>
    /// The viewer's spelling because this is a name a person typed after reading it off the model,
    /// and it is shown back to them in the same box they typed it into — storing the survey rows'
    /// own reading of it would answer an administrator with a name they cannot find anywhere on the
    /// model they were looking at. Either spelling is accepted when it is set, and the altitude
    /// lookup matches a station under either of its names, so nothing downstream has to know which
    /// one this is.
    /// </para>
    /// </summary>
    public string? ReferenceStationName { get; set; }

    /// <summary>
    /// Station/survey name prefixes depth matching is limited to; empty means the whole model.
    /// </summary>
    public string[] DepthFilter { get; set; } = [];

    public DateTimeOffset? ArmedAt { get; set; }

    public DateTimeOffset? ClosedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => TripLogId.ToString();

    public string? RootEntityType => nameof(TripLog);

    public string? RootEntityId => TripLogId.ToString();
}
