// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Import;

/// <summary>
/// Where a file's recorded position came from.
///
/// <para>
/// Stored rather than inferred, and the reason is <see cref="Manual"/>: a position somebody
/// placed by hand must not be quietly replaced by a later pass over the bytes, and somebody
/// dragging a picture onto the map over a fix the camera already recorded has to be told what
/// they are about to overwrite. Neither is answerable from the point alone.
/// </para>
/// </summary>
public enum PhotoPositionSource : short
{
    /// <summary>No position: the file records none and nobody has given it one.</summary>
    None = 0,

    /// <summary>Read from the file's own capture metadata.</summary>
    Exif = 1,

    /// <summary>Worked out by matching the capture time against a recorded track.</summary>
    TrackMatch = 2,

    /// <summary>
    /// A person settled this picture's position, and nothing automatic overwrites it. That
    /// includes settling it at "there is none": a picture whose position was deliberately taken
    /// off carries this with no point, so a later pass over the bytes does not put the camera's
    /// own guess back.
    /// </summary>
    Manual = 3,
}

/// <summary>
/// What a photo review does with the whole drop rather than with one candidate: what the
/// created objects become and are bound to, how far apart two pictures are still one place,
/// how far it looks for something already in the registry, and how a picture with no fix of
/// its own may get one.
/// </summary>
public sealed record PhotoImportOptions
{
    /// <summary>
    /// The trip these pictures belong to. Everything created is attached to it as well as to
    /// the object it created, which is what makes "these photos are from Saturday" a single
    /// action rather than a second pass over the same list.
    /// </summary>
    public Guid? TripLogId { get; init; }

    /// <summary>What a candidate becomes unless its own row says otherwise.</summary>
    public ImportTargetKind DefaultKind { get; init; } = ImportTargetKind.CaveEntrance;

    public string? DefaultCaveTypeCode { get; init; }

    public string? DefaultEntranceTypeCode { get; init; }

    public string? DefaultFeatureTypeCode { get; init; }

    /// <summary>
    /// How far apart two pictures can be and still be the same place. Twelve photographs of one
    /// entrance are one candidate with a gallery, not twelve points to reject one at a time —
    /// and the number is the reviewer's, because a shaft photographed from the rim and from the
    /// bottom of the doline is two positions thirty metres apart and one hole.
    /// </summary>
    public double ClusterRadiusMeters { get; init; } = DefaultClusterRadiusMeters;

    /// <summary>How far the proximity list looks for objects already in the registry.</summary>
    public double ProximityRadiusMeters { get; init; } = DefaultProximityRadiusMeters;

    /// <summary>
    /// Visibility every created object starts with. Most restrictive by default, for the same
    /// reason a vector import is: a drop is a bulk action, and a bulk action that publishes by
    /// default publishes a whole trip's worth of holes at once.
    /// </summary>
    public Visibility Visibility { get; init; } = Visibility.Private;

    public Guid? CavingGroupId { get; init; }

    /// <summary>Marks every created object a protection root.</summary>
    public bool LocationProtected { get; init; }

    public IReadOnlyList<long> TagIds { get; init; } = [];

    public string? NamePrefix { get; init; }

    /// <summary>
    /// What happens to the altitude the camera recorded. A phone's GPS altitude is the worst of
    /// the three numbers it reports, so discarding it is a real answer rather than a way of
    /// losing data — and, as everywhere else here, the file is the only place one can come from.
    /// </summary>
    public ImportElevationPolicy Elevation { get; init; } = ImportElevationPolicy.Discard;

    /// <summary>
    /// The uploaded track a picture with no fix of its own is placed against by its capture
    /// time. Null leaves such pictures unplaced, which is what they are.
    /// </summary>
    public Guid? TrackGeofileId { get; init; }

    /// <summary>
    /// Seconds to add to a picture's stated capture time before comparing it with the track.
    /// This field is the whole reason time-based placement works at all: camera clocks drift,
    /// and a camera set to the wrong zone is out by whole hours while looking perfectly
    /// plausible.
    /// </summary>
    public int CameraClockOffsetSeconds { get; init; }

    /// <summary>
    /// How far from a recorded fix, in seconds, a picture may still be placed. Beyond it the
    /// track says nothing about where the photographer was — they may have stopped, or the unit
    /// may have lost the sky — and a guessed position that looks measured is worse than none.
    /// </summary>
    public int TrackMatchToleranceSeconds { get; init; } = DefaultTrackMatchToleranceSeconds;

    public const double DefaultClusterRadiusMeters = 25;

    public const double DefaultProximityRadiusMeters = 80;

    /// <summary>Widest proximity search offered — a bulk query over the registry, so it is bounded.</summary>
    public const double MaxProximityRadiusMeters = 5000;

    /// <summary>Widest clustering offered; past this a "place" is a hillside.</summary>
    public const double MaxClusterRadiusMeters = 1000;

    public const int DefaultTrackMatchToleranceSeconds = 120;

    /// <summary>A whole day either side. Past that a track is not evidence about a picture.</summary>
    public const int MaxTrackMatchToleranceSeconds = 86_400;

    /// <summary>A day of clock drift in either direction, which covers a wrong date as well as a wrong zone.</summary>
    public const int MaxCameraClockOffsetSeconds = 86_400;

    /// <summary>
    /// The most pictures one review holds. A drop is a trip's worth of photographs, and every
    /// one of them is a stored file that has to be read, clustered and compared against the
    /// registry; past this the review stops being something a person goes through.
    /// </summary>
    public const int MaxFiles = 500;
}

/// <summary>
/// What the reviewer decided about one candidate — one place, however many pictures of it.
/// Every field beyond <see cref="Action"/> overrides what the drop's own options proposed, so
/// a reviewer who agrees with a row stores nothing but the row's existence.
/// </summary>
public sealed record PhotoDecision
{
    public ImportDecisionAction Action { get; init; } = ImportDecisionAction.Create;

    public ImportTargetKind? Kind { get; init; }

    public string? FeatureTypeCode { get; init; }

    public string? CaveTypeCode { get; init; }

    public string? EntranceTypeCode { get; init; }

    public string? Name { get; init; }

    /// <summary>
    /// For <see cref="ImportDecisionAction.Attach"/>, the existing object these pictures belong
    /// to — the common case, and the one the proximity list exists to make a single press. For a
    /// <see cref="ImportTargetKind.CaveEntrance"/> being created, the cave it becomes an
    /// entrance of.
    /// </summary>
    public Guid? AttachToFeatureId { get; init; }

    /// <summary>Whether this candidate keeps the altitude its pictures carry; null follows the drop's policy.</summary>
    public bool? KeepElevation { get; init; }

    /// <summary>
    /// A position the reviewer gave this candidate by dragging it onto the map, as
    /// [longitude, latitude]. It beats everything the files say — it is the only source with a
    /// person behind it — and it is written to the review, never back into the pictures.
    /// </summary>
    public IReadOnlyList<double>? Position { get; init; }
}
