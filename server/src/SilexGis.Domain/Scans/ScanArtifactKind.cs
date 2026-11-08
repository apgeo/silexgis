// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Scans;

/// <summary>
/// What a file produced by a scan's processing is, typed so a step can say what it takes and what
/// it makes. Stored as smallint; values are part of the schema contract — append, never renumber.
/// </summary>
public enum ScanArtifactKind : short
{
    /// <summary>A point cloud as a LAS or LAZ file: the uploaded original, or a tool's export.</summary>
    PointCloudLaz = 0,

    /// <summary>A thinned copy of a point cloud, made for display when the full cloud is too large to stream.</summary>
    PointCloudLazDisplay = 1,

    /// <summary>A point cloud converted into the tiles a viewer streams, packed into one archive.</summary>
    PointCloudTiles = 2,

    /// <summary>The path the scanner travelled, as a line.</summary>
    Trajectory = 3,

    /// <summary>A human-readable account of a run: what was done, with what, and what came out.</summary>
    Report = 4,

    /// <summary>One file of a raw scanner recording as it was uploaded: a lidar chunk, an IMU log, a serial-number, GNSS or calibration file.</summary>
    RawRecording = 5,

    /// <summary>An HDMapping session — the registered chunks, their poses and the parameters used — packed into one archive, without the odometry's cache.</summary>
    HdmSession = 6,

    /// <summary>A picture of the scan seen from above.</summary>
    PlanImage = 7,

    /// <summary>A picture of the scan unrolled along its centreline, seen from the side.</summary>
    ProfileImage = 8,

    /// <summary>A surface reconstructed from the points, as a mesh.</summary>
    MeshStl = 9,

    /// <summary>The scanner's path simplified into a line through the passages, in the scan's own frame.</summary>
    Centreline = 10,
}
