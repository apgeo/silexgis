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

    /// <summary>The survey model cavers are placed in; chosen per trip by the admin.</summary>
    public Guid? SurveyModelId { get; set; }

    /// <summary>
    /// The model's cave, snapshotted when the model is chosen — the protection anchor for
    /// the config's own station vocabulary (reference station, depth filter), evaluable
    /// after the model is gone. Null with station names present reads as withheld.
    /// </summary>
    public Guid? CaveFeatureId { get; set; }

    /// <summary>
    /// Station whose altitude is the depth datum (viewer spelling); null means the model's
    /// highest entrance-flagged station.
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
