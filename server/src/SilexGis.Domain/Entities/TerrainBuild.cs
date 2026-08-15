// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Entities;

/// <summary>
/// Lifecycle of one terrain build. Stored as smallint and never renumbered.
/// </summary>
/// <remarks>
/// The same four states the processing queue uses, so a reader who knows one knows the other.
/// The build row carries them rather than deriving them from its queue row because the queue is
/// machinery shared with every other kind of background work, and because a build outlives the
/// job that made it: rows are kept until somebody deletes them, and a job record is not.
/// </remarks>
public enum TerrainBuildStatus : short
{
    /// <summary>Accepted and waiting for a worker. Nothing has been fetched or written yet.</summary>
    Queued = 0,

    /// <summary>A worker has it. <see cref="TerrainBuild.Phase"/> says where it has got to.</summary>
    Running = 1,

    /// <summary>Finished, validated, and safe to publish.</summary>
    Succeeded = 2,

    /// <summary>
    /// Stopped without producing a usable pyramid. <see cref="TerrainBuild.ErrorCode"/> says why
    /// in a form a screen can translate; <see cref="TerrainBuild.Message"/> says it in words.
    /// </summary>
    Failed = 3,
}

/// <summary>
/// How far along the chain a terrain build has got. Stored as smallint and never renumbered.
/// </summary>
/// <remarks>
/// <para>
/// The values are written out one by one, in the order they happen, because a build is a long
/// chain of unlike work — obtaining rasters, reprojecting them, meshing them, checking the mesh,
/// putting it where it is served — and a screen that reports "running" for two hours tells an
/// administrator nothing. Naming the steps makes "which step" a decision somebody took rather
/// than whatever the code happened to be doing.
/// </para>
/// <para>
/// The order is meaningful: each step consumes what the one before it produced. It is not a
/// state machine that can be entered anywhere — a build that resumes after a restart re-enters
/// at the earliest step whose output is missing or does not check out.
/// </para>
/// </remarks>
public enum TerrainBuildPhase : short
{
    /// <summary>Nothing has started. The state of a build that is still queued.</summary>
    Pending = 0,

    /// <summary>Obtaining elevation rasters — downloaded, uploaded, or read from a directory.</summary>
    Fetch = 1,

    /// <summary>
    /// Turning those rasters into one consistent input: mosaic, reprojection, nodata, resolution.
    /// </summary>
    Prepare = 2,

    /// <summary>Meshing the prepared rasters into a tile pyramid.</summary>
    Bake = 3,

    /// <summary>
    /// Reading the pyramid back and refusing it if it is damaged. A broken pyramid answers every
    /// request successfully and draws nothing at all, so this step is the only thing standing
    /// between a bad bake and a globe that is silently flat.
    /// </summary>
    Validate = 4,

    /// <summary>Moving the checked pyramid to where it is served from.</summary>
    Publish = 5,
}

/// <summary>Where one of a build's input rasters came from. Stored as smallint — do not renumber.</summary>
public enum TerrainBuildSourceKind : short
{
    /// <summary>Downloaded by the application from a public dataset, for the extent asked for.</summary>
    Fetched = 0,

    /// <summary>Uploaded through the browser. The path for the modest files that fit that way.</summary>
    Uploaded = 1,

    /// <summary>
    /// Read from a directory on the server that the operator has listed as readable. The path for
    /// national lidar, which arrives as tens of gigabytes from a portal that cannot be scripted.
    /// </summary>
    ServerDirectory = 2,
}

/// <summary>
/// One run of the terrain pipeline: the area asked for, what it was made from, how far it got,
/// and — once it succeeds — the pyramid it produced.
/// </summary>
/// <remarks>
/// <para>
/// An installation asset with no owner, no caving group and no audience. It is built from public
/// elevation data and holds no cave position, so it carries none of the owner/group/visibility
/// facts that content rows carry, and every permission question about it is asked of the terrain
/// domain as a whole rather than of this row. That is also why it is safe for the listing to be
/// unfiltered once the domain right is established: there is no per-row audience to filter to.
/// </para>
/// <para>
/// Progress lives here rather than on the shared processing queue. The screen that watches a
/// build is already polling this row for its sources, extent, size and active flag, so a second
/// polled resource would be pure overhead; no other kind of background work reports progress at
/// all; and the queue table is shared machinery that should not grow columns for one feature.
/// The field names follow the vocabulary a long-running operation is conventionally described
/// with — status, progress, message, created/started/finished/updated — which costs nothing here
/// and means a later machine-readable status surface has nothing to rename.
/// </para>
/// <para>
/// The height datum is a property of <i>this build</i>, not of the installation, because it
/// describes what the tiles this build produced actually contain. An installation that swapped
/// its terrain and kept one global correction would silently move every cave it draws by the
/// geoid undulation — around forty metres over Romanian karst, with nothing on screen to say so.
/// </para>
/// <para>
/// <b>Writes to this row are audited, so the machine's own chatter must not go through the
/// tracked-save path.</b> The acts worth a trail entry are the human ones — starting a build,
/// making one the terrain the scene draws, deleting one. A worker nudging
/// <see cref="Progress"/> and <see cref="Message"/> every few seconds for the length of a bake
/// would bury those under thousands of entries recording that a number went up. Progress and
/// phase are therefore written as a direct update statement rather than by tracking and saving
/// the entity, which also keeps two concurrent writers from overwriting each other's fields —
/// and such a write must set the updated timestamp itself, because the interceptor that
/// normally maintains it only sees tracked saves.
/// </para>
/// </remarks>
public class TerrainBuild : ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>
    /// The ground this build covers, as drawn on the map (SRID 4326). Held as a geometry rather
    /// than four numbers so that "is there terrain over this cave" is a question the database can
    /// answer, and so it cannot disagree with every other extent in the system about what a
    /// bounding box means.
    /// </summary>
    public required Polygon Extent { get; set; }

    /// <summary>
    /// The deepest pyramid level to bake, as asked for.
    /// </summary>
    /// <remarks>
    /// Always recorded explicitly, never left to the mesher to infer. Inferred depth is derived
    /// from the finest input raster, so a small patch of half-metre lidar inside a coarse region
    /// pushes the whole bake several levels deeper and multiplies the tile count — and cost tracks
    /// tile count, not area. What is actually achievable is capped by the data anyway: thirty-metre
    /// sources run out of detail around level 13 whatever is asked for here, and the pyramid simply
    /// stops advertising deeper levels where nothing finer exists.
    /// </remarks>
    public int RequestedMaxDepth { get; set; }

    public TerrainBuildStatus Status { get; set; } = TerrainBuildStatus.Queued;

    public TerrainBuildPhase Phase { get; set; } = TerrainBuildPhase.Pending;

    /// <summary>Completion of the whole build, 0–100. Bounded by the database, not by trust.</summary>
    public int Progress { get; set; }

    /// <summary>
    /// What the build is doing now, in words, for whoever is watching it. Free text written by
    /// the worker; a failure's stable, translatable reason is <see cref="ErrorCode"/> instead.
    /// </summary>
    public string? Message { get; set; }

    /// <summary>
    /// Why a build failed, as a short stable code a screen can translate — never a raw exception.
    /// </summary>
    /// <remarks>
    /// The tools this pipeline drives fail with stack dumps and multi-kilobyte native error text.
    /// Storing one of those verbatim risks overflowing the column <i>while recording the failure</i>,
    /// which leaves a build stuck as running forever — a failure to record a failure. So the worker
    /// decides on a short reason of its own and puts the raw text, truncated, in
    /// <see cref="LogTail"/>.
    /// </remarks>
    public string? ErrorCode { get; set; }

    /// <summary>
    /// The last part of the tool output, kept so a failure can be diagnosed without shell access
    /// to the server. Deliberately a bounded tail and not a log: the interesting part of a failed
    /// run is its end, and an unbounded column here would be a way to fill the database with the
    /// output of a tool that is having a bad day.
    /// </summary>
    public string? LogTail { get; set; }

    /// <summary>Bytes the finished pyramid and its retained intermediates occupy on disk.</summary>
    /// <remarks>
    /// Shown wherever builds are listed. Builds are kept until somebody deletes them, so the size
    /// is what makes that an informed choice rather than a surprise when the disk fills.
    /// </remarks>
    public long? SizeBytes { get; set; }

    /// <summary>
    /// The version string stamped into the finished pyramid's manifest, derived from the tiles it
    /// actually contains.
    /// </summary>
    /// <remarks>
    /// Every tile URL a viewer requests ends with this string, so it is the cache key for the whole
    /// pyramid. Derived from the content rather than from the clock: re-running a bake that produces
    /// the same tiles leaves browser caches warm, while any change to any tile changes every URL.
    /// A constant here would mean a re-bake is served entirely out of viewers' caches with no
    /// request reaching the server — the ground drawn from heights the installation no longer has.
    /// </remarks>
    public string? PyramidVersion { get; set; }

    /// <summary>What the heights in this build's tiles are measured from.</summary>
    public TerrainHeightDatum HeightDatum { get; set; } = TerrainHeightDatum.Orthometric;

    /// <summary>
    /// The local geoid undulation in metres, used only when this build's heights are ellipsoidal.
    /// </summary>
    public double GeoidHeightM { get; set; }

    /// <summary>
    /// Metres to add to a surveyed altitude so it sits on the ground this build draws. Resolved by
    /// the one function that owns the rule, so this build and a terrain source configured by the
    /// operator can never disagree about which way the correction goes.
    /// </summary>
    public double SurveyHeightOffsetM => GeoidOffset.SurveyToSceneOffsetM(HeightDatum, GeoidHeightM);

    /// <summary>
    /// Whether this is the build the 3D scene draws. At most one row holds it, enforced by a unique
    /// index rather than by a handler: left to the write path alone it would be a rule that holds
    /// until two people press the button at once, and then "which terrain is current" would be
    /// decided by whichever row happened to be read first.
    /// </summary>
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>When a worker picked the build up.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When the build stopped, whether it succeeded or failed.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}

/// <summary>
/// One raster that went into a build, and the credit the data it holds requires.
/// </summary>
/// <remarks>
/// The credit is recorded per source rather than assumed, because a pyramid must never carry a
/// false one: a build that mixes a national lidar tile into a European elevation model owes both
/// statements, and a single hardcoded credit would name whichever dataset the tooling was first
/// written against. Licence obligations are recorded here and not enforced anywhere — recording
/// them is what makes enforcing them possible later.
/// </remarks>
public class TerrainBuildSource
{
    public long Id { get; set; }

    public Guid TerrainBuildId { get; set; }

    public TerrainBuildSourceKind Kind { get; set; }

    /// <summary>
    /// What names this source, in whatever terms its kind uses: the dataset and cell for a
    /// download, the stored path for an upload, the directory for one the operator supplied.
    /// </summary>
    public required string Reference { get; set; }

    /// <summary>The credit this data requires, as it will appear in the pyramid's manifest.</summary>
    public required string Attribution { get; set; }

    /// <summary>The licence the data is held under, recorded verbatim as the publisher states it.</summary>
    public string? Licence { get; set; }
}
