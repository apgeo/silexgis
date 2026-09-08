// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.Terrain;

namespace SilexGis.Domain.Entities;

/// <summary>How far a computed picture of the ground has got.</summary>
/// <remarks>
/// Deliberately its own list rather than the one an uploaded raster moves through. That one says
/// what happened to bytes somebody sent — uploaded, converted, ready — and its words are shown to
/// the person who sent them. Nobody uploads a hillshade: it is asked for, computed and either
/// arrives or does not, and a failure here means the computation failed over elevation that is
/// itself perfectly fine. Stored as smallint, append-only, never renumbered.
/// </remarks>
public enum TerrainDerivativeStatus : short
{
    /// <summary>Asked for, waiting for a worker.</summary>
    Queued = 0,

    /// <summary>Being computed now.</summary>
    Computing = 1,

    /// <summary>Computed, and its rasters are on disk.</summary>
    Ready = 2,

    /// <summary>The computation failed; the reason is on the row.</summary>
    Failed = 3,
}

/// <summary>
/// One computed picture of the ground — a shaded relief, a steepness map, a facing map — recorded
/// as what it is, what it was computed from, and what was asked for when it was computed.
/// </summary>
/// <remarks>
/// <para>
/// A table of its own, and not a row in the catalogue of uploaded overlays, because the two answer
/// different questions. An uploaded overlay is a file a person chose to put on the map: it has an
/// owner, an audience, a kind somebody picked from a list, and a single file. This has none of
/// those and needs five things that one cannot hold — which elevation it was computed from, which
/// arithmetic, with which settings, by which run, and how many times it has been recomputed. Put
/// here, those are columns; put on the upload row they would be absent, and the consequence is
/// specific and bad: a picture computed from elevation that has since been replaced would go on
/// being served with nothing on it saying so, and a shaded relief that disagrees with the ground
/// beneath it reads as a fault in the cave data rather than in the tile.
/// </para>
/// <para>
/// One row is one picture of one build. Its rasters are separate rows because a build's elevation
/// is several files at several pixel sizes, never merged onto a common grid, so a picture of a
/// build is one output file per input file.
/// </para>
/// <para>
/// Nothing here records whether the row is stale, on purpose. Staleness is
/// <b>this row's build is not the build the installation currently serves</b>, and that is one
/// query against a fact the database already holds under a unique index. A column saying the same
/// thing would be written by whatever activated a build, and the first activation that failed
/// between its two writes would leave a superseded picture flagged current — which is exactly the
/// failure the flag was added to prevent.
/// </para>
/// </remarks>
public class TerrainDerivativeLayer : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The elevation build this picture was computed from.</summary>
    public Guid TerrainBuildId { get; set; }

    /// <summary>Which picture it is.</summary>
    public TerrainDerivative Derivative { get; set; }

    /// <summary>What was asked for, as it was asked, in JSON.</summary>
    /// <remarks>
    /// Kept verbatim rather than spread over columns because the settings differ per picture — a
    /// hillshade has a light, a slope has a unit, a colour relief has a whole ramp — and because
    /// what has to survive is the ability to say what produced the file on screen. Nothing queries
    /// inside it; the one thing that is asked of it is whether two requests are the same request,
    /// and <see cref="SettingsHash"/> answers that.
    /// </remarks>
    public string Settings { get; set; } = "{}";

    /// <summary>A fingerprint of <see cref="Settings"/>, so one picture is asked for once.</summary>
    public required string SettingsHash { get; set; }

    /// <summary>What to call this layer where a person reads it.</summary>
    public required string Name { get; set; }

    public TerrainDerivativeStatus Status { get; set; } = TerrainDerivativeStatus.Queued;

    /// <summary>The short code for why the computation failed, when it did.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>A sentence about the failure, in words a person can act on.</summary>
    public string? Message { get; set; }

    /// <summary>The run that produced what is stored, or is producing it now.</summary>
    /// <remarks>
    /// The link runs this way round because it is the row that is read and the run that is looked
    /// up from it. A job carries only the key of its subject, so without this column the question
    /// "which run wrote this picture" is answered by searching every job's payload text.
    /// </remarks>
    public long? ProcessingJobId { get; set; }

    /// <summary>
    /// How many times this row's rasters have been computed, counting from one.
    /// </summary>
    /// <remarks>
    /// Rasters are replaced in place when a picture is recomputed with the same settings over the
    /// same build — a re-run after a failure, or after the elevation was prepared again. Anything
    /// holding a file it fetched earlier needs a way to tell that the bytes behind the same address
    /// changed, and a timestamp cannot be that: two recomputations within the same second are
    /// indistinguishable by one, and a clock that goes backwards makes the newer file look older.
    /// </remarks>
    public int Version { get; set; }

    /// <summary>Everything this picture takes on disk, in bytes.</summary>
    /// <remarks>
    /// Summed and stored rather than counted on demand, because it is the number that has to be
    /// visible on a listing: a picture is computed per build and per settings, so the disk they
    /// occupy multiplies quietly, and terrain lives on a volume that is neither backed up nor
    /// swept.
    /// </remarks>
    public long SizeBytes { get; set; }

    /// <summary>When the rasters currently stored were finished.</summary>
    public DateTimeOffset? ComputedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>One file of a computed picture, over one of its build's elevation rasters.</summary>
public class TerrainDerivativeRaster
{
    public long Id { get; set; }

    public Guid TerrainDerivativeLayerId { get; set; }

    /// <summary>The elevation raster this was computed from, as an absolute path.</summary>
    /// <remarks>
    /// Recorded so that a picture can be matched back to the ground it was drawn from when a build
    /// has several rasters covering different tiles at different resolutions. Absolute, matching how
    /// the elevation rasters themselves are recorded — which means both are read against the
    /// directory this installation currently keeps builds in, and moving that directory invalidates
    /// them together rather than one of them.
    /// </remarks>
    public required string SourcePath { get; set; }

    /// <summary>The computed file, as an absolute path.</summary>
    public required string Path { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    public double PixelSizeDegrees { get; set; }

    /// <summary>The ground this file covers.</summary>
    public required Polygon Footprint { get; set; }

    public long SizeBytes { get; set; }

    public DateTimeOffset ComputedAt { get; set; }
}
