// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Surveys;

/// <summary>
/// The stable codes a survey upload's processing run fails with, for a screen — the device's
/// result dialog, the web status page — to translate. They are part of the exchange a field device
/// speaks: append, never rename.
/// </summary>
public static class SurveyPipelineFailures
{
    /// <summary>
    /// The survey archive is not one this server can read: no manifest, the manifest not its first
    /// entry, no survey dump, a database version older than the tool's own importer reads, an
    /// entry whose name climbs out of the archive, or not a ZIP at all.
    /// </summary>
    public const string ArchiveInvalid = "survey_pipeline.archive_invalid";

    /// <summary>
    /// The survey archive unpacks to more than this installation allows, holds more entries than
    /// it allows, or an entry expands far beyond what it declares — the shape of a decompression
    /// bomb.
    /// </summary>
    public const string ArchiveTooLarge = "survey_pipeline.archive_too_large";

    /// <summary>
    /// A file the upload archived when it arrived can no longer be found in the file store. The
    /// run cannot read what is not there; the upload's own rows are untouched.
    /// </summary>
    public const string FilesMissing = "survey_pipeline.files_missing";

    /// <summary>Compiling was asked for and no Therion source came with the upload.</summary>
    public const string SourceMissing = "survey_pipeline.source_missing";

    /// <summary>
    /// A Therion source names a file outside the upload — an absolute path, a path that climbs
    /// out of the project, or a file that was not sent.
    /// </summary>
    public const string SourceInvalid = "survey_pipeline.source_invalid";

    /// <summary>The compiler this installation is configured with cannot be reached.</summary>
    public const string CompilerUnavailable = "survey_pipeline.compiler_unavailable";

    /// <summary>The compiler ran and reported the survey could not be compiled.</summary>
    public const string CompileFailed = "survey_pipeline.compile_failed";

    /// <summary>The compiler ran past the time this installation allows a compile.</summary>
    public const string CompileTimeout = "survey_pipeline.compile_timeout";

    /// <summary>
    /// An output that was asked for was not produced. On the artefact list, per output, while the
    /// run succeeds on what was produced; the run's own failure only when none of it was.
    /// </summary>
    public const string OutputMissing = "survey_pipeline.output_missing";

    /// <summary>Nothing the compiler produced could be stored.</summary>
    public const string StoreFailed = "survey_pipeline.store_failed";

    /// <summary>
    /// The survey is fixed in a coordinate system no EPSG code is known for, so its model is stored
    /// and not read into stations. On the Therion model's artefact entry; the run still succeeds.
    /// </summary>
    public const string CrsUnknown = "survey_pipeline.crs_unknown";

    /// <summary>The uploaded point cloud is not a LAS or LAZ file the server can read: its header is missing, short or inconsistent.</summary>
    public const string PointCloudInvalid = "survey_pipeline.point_cloud_invalid";

    /// <summary>
    /// The uploaded raw recording is not one the server can process: a lidar chunk whose header
    /// does not read, an IMU log without the expected columns, or a chunk missing its pair.
    /// </summary>
    public const string RecordingInvalid = "survey_pipeline.recording_invalid";

    /// <summary>
    /// Processing is switched on, but no point-cloud worker has announced itself, or the one that
    /// has cannot run a step the recipe asks for.
    /// </summary>
    public const string WorkerUnavailable = "survey_pipeline.worker_unavailable";

    /// <summary>Processing is switched on and a job was left for the worker, and nothing took it in the time allowed.</summary>
    public const string WorkerSilent = "survey_pipeline.worker_silent";

    /// <summary>The worker took the job and answered that it failed, refused it, or was stopped before finishing it.</summary>
    public const string WorkerFailed = "survey_pipeline.worker_failed";

    /// <summary>The worker ran past the time this installation allows one job.</summary>
    public const string WorkerTimeout = "survey_pipeline.worker_timeout";

    /// <summary>
    /// The worker was stopped (restarted, or its container replaced) while it ran a step, and its
    /// half-written outputs are not trusted: a half-run odometry leaves a session that looks whole
    /// and is not. Running the upload again starts the step afresh; nothing restarts it unasked.
    /// </summary>
    public const string WorkerInterrupted = "survey_pipeline.worker_interrupted";

    /// <summary>
    /// The point cloud a tool exported from a raw recording failed its check — points missing, a
    /// coordinate that is not a number, points outside the header's bounds, a trajectory whose time
    /// goes back — and nothing was built on it.
    /// </summary>
    public const string ExportInvalid = "survey_pipeline.export_invalid";

    /// <summary>
    /// A processing step was not started because what it is estimated to write does not fit: under
    /// the account's quota, the installation's cap, the scans' cap, or on the disk of the file store
    /// or the worker. Checked before each step, so a run is refused rather than left half-done; the
    /// message says how much is missing.
    /// </summary>
    public const string StorageInsufficient = "survey_pipeline.storage_insufficient";

    /// <summary>
    /// A surface was made of a scan but could not be saved as a cave's walls: the scan is no longer
    /// placed, or the cave it was asked for is gone. The scan keeps the surface as its own product.
    /// </summary>
    public const string SurfaceUnplaced = "survey_pipeline.surface_unplaced";

    /// <summary>
    /// Something went wrong that the run did not expect. The server's own log has the whole of it;
    /// the upload's row says only that, because the inside of the server is not the uploader's to
    /// read.
    /// </summary>
    public const string Unexpected = "survey_pipeline.unexpected";
}

/// <summary>
/// A processing run stopped for a reason it can name. The message is written for the person who
/// sent the survey and must carry no coordinate, no station name and nothing from the inside of
/// the server.
/// </summary>
public sealed class SurveyPipelineException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>
/// How far along the whole run each stage is: the progress a device's bar shows is one number for
/// the run, and every stage owns a band of it, in the order the stages happen.
/// </summary>
/// <remarks>
/// There are three runs: a survey archive goes Archive, Unpack, Scaffold, Compile, Store, Read; a
/// point cloud goes Archive, Inspect, Downsample, Tile, Store; a raw scanner recording goes
/// Archive, Inspect, Odometry, Export, Check, Downsample, Tile, Store. The survey and the point
/// cloud runs share one band per stage, so for them a band is a fact about the stage. The
/// recording's run spends most of its time in odometry, which has to sit between stages the
/// point cloud's run already places end to end; it has bands of its own, asked for with the
/// origin, and a run of it reports through <see cref="Band(SurveyUploadStage, SurveyOrigin)"/>.
/// A run that ends early (nothing to compile, no tiler) jumps to 100 when it succeeds, so the last
/// stage of a run need not end at 100; what matters is that bands do not overlap within one run.
/// </remarks>
public static class SurveyPipelineProgress
{
    /// <summary>The band a stage owns, as the run's percentage at its start and at its end.</summary>
    public static (int Start, int End) Band(SurveyUploadStage stage) => stage switch
    {
        SurveyUploadStage.Archive => (0, 5),
        SurveyUploadStage.Unpack => (5, 15),
        SurveyUploadStage.Scaffold => (15, 25),
        SurveyUploadStage.Compile => (25, 80),
        SurveyUploadStage.Store => (80, 95),
        SurveyUploadStage.Read => (95, 100),
        SurveyUploadStage.Inspect => (5, 15),
        SurveyUploadStage.Downsample => (15, 25),
        SurveyUploadStage.Tile => (25, 80),
        SurveyUploadStage.Odometry => RecordingBand(SurveyUploadStage.Odometry),
        SurveyUploadStage.Export => RecordingBand(SurveyUploadStage.Export),
        SurveyUploadStage.Check => RecordingBand(SurveyUploadStage.Check),
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>The band a stage owns in a run of an upload of this origin.</summary>
    public static (int Start, int End) Band(SurveyUploadStage stage, SurveyOrigin origin) =>
        origin == SurveyOrigin.Mandeye ? RecordingBand(stage) : Band(stage);

    // A raw recording's run: odometry runs for longer than the recording lasted, so it owns the
    // largest share; tiling, which follows, still takes minutes and keeps a share of its own.
    private static (int Start, int End) RecordingBand(SurveyUploadStage stage) => stage switch
    {
        SurveyUploadStage.Archive => (0, 5),
        SurveyUploadStage.Inspect => (5, 10),
        SurveyUploadStage.Odometry => (10, 50),
        SurveyUploadStage.Export => (50, 55),
        SurveyUploadStage.Check => (55, 60),
        SurveyUploadStage.Downsample => (60, 65),
        SurveyUploadStage.Tile => (65, 80),
        SurveyUploadStage.Store => (80, 95),
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>The stages a run of an upload of this origin goes through, in order.</summary>
    public static IReadOnlyList<SurveyUploadStage> Order(SurveyOrigin origin) => origin switch
    {
        SurveyOrigin.PointCloud =>
        [
            SurveyUploadStage.Archive, SurveyUploadStage.Inspect, SurveyUploadStage.Downsample,
            SurveyUploadStage.Tile, SurveyUploadStage.Store,
        ],
        SurveyOrigin.Mandeye =>
        [
            SurveyUploadStage.Archive, SurveyUploadStage.Inspect, SurveyUploadStage.Odometry,
            SurveyUploadStage.Export, SurveyUploadStage.Check, SurveyUploadStage.Downsample,
            SurveyUploadStage.Tile, SurveyUploadStage.Store,
        ],
        _ =>
        [
            SurveyUploadStage.Archive, SurveyUploadStage.Unpack, SurveyUploadStage.Scaffold,
            SurveyUploadStage.Compile, SurveyUploadStage.Store, SurveyUploadStage.Read,
        ],
    };

    /// <summary>
    /// The run's percentage when a stage is <paramref name="percentOfStage"/> of the way through.
    /// A stage's own figure outside 0–100 is brought inside it, so a misreporting step can never
    /// move the bar backwards past its stage or forwards past the next.
    /// </summary>
    public static int Overall(SurveyUploadStage stage, int percentOfStage) => Within(Band(stage), percentOfStage);

    /// <summary>The run's percentage for a stage of a run of an upload of this origin, as <see cref="Overall(SurveyUploadStage, int)"/>.</summary>
    public static int Overall(SurveyUploadStage stage, int percentOfStage, SurveyOrigin origin) =>
        Within(Band(stage, origin), percentOfStage);

    private static int Within((int Start, int End) band, int percentOfStage)
    {
        var within = Math.Clamp(percentOfStage, 0, 100);
        return band.Start + ((band.End - band.Start) * within / 100);
    }
}

/// <summary>
/// What a run that ended without compiling says, so a device can tell the caver why there is no
/// model even though nothing went wrong.
/// </summary>
public static class SurveyPipelineNotes
{
    public const string NotAsked = "Archived and read. Compiling was not asked for.";

    public const string NoCompiler = "Archived and read. Not compiled: this server has no survey compiler.";

    /// <summary>
    /// What a point-cloud run says when it stops after reading the header because this server has
    /// no worker to tile with. The same family of words as <see cref="NoCompiler"/>, because it is
    /// the same situation — the file is kept and described, and a rerun once a worker is installed
    /// finishes the work.
    /// </summary>
    public const string NoTiler = "Archived and inspected. Not tiled: this server has no point-cloud worker.";

    /// <summary>
    /// What a raw recording's run says when it stops after inspecting the recording because this
    /// server has no worker that runs HDMapping. The recording is kept and described; a rerun once
    /// such a worker is installed processes it.
    /// </summary>
    public const string NoOdometry = "Archived and inspected. Not processed: this server has no worker that runs HDMapping.";

    /// <summary>
    /// The note of a point-cloud run that estimated a cloud over the installation's thresholds
    /// and stopped, because nobody has yet chosen how much of it to show. Tiling the whole of a
    /// cloud that size takes hours of worker time and gigabytes of tiles, which is not spent on
    /// anybody's behalf unasked; a rerun with a choice carries on from the estimate.
    /// </summary>
    public static string ChoiceNeeded(long pointsInFile) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Estimated: {pointsInFile:N0} points. Choose how much to show, then run again.");

    /// <summary>
    /// The note of a scan's run that reached a step done on another computer and stopped there:
    /// its inputs are offered as a download, and uploading what the step makes carries the run on.
    /// </summary>
    public static string AwaitingExternal(string stepName) =>
        $"Waiting for a step done on another computer: {stepName}. Download its inputs, run it, then upload what it made.";

    /// <summary>The note of a point-cloud run that tiled the cloud: how much was shown and what it came to.</summary>
    public static string Tiled(long pointsTiled, long pointsInFile, long tileBytes, bool reused) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Tiled: {pointsTiled:N0} of {pointsInFile:N0} points, {tileBytes / 1_048_576d:N1} MB of tiles.")
        + (reused ? " The tiles of an earlier run were kept, nothing about them having changed." : "");

    /// <summary>
    /// The note a run ends with when it stops after reading the archive, or null when it goes on
    /// to compile.
    /// </summary>
    public static string? WithoutCompiling(bool compileAsked, bool compilerAvailable) =>
        !compileAsked ? NotAsked
        : !compilerAvailable ? NoCompiler
        : null;

    /// <summary>Added to the note of a compiled run whose model could not be placed, and so was not read.</summary>
    public const string ModelUnplaced =
        " The survey is fixed to no position, so its model was stored but not read into stations.";

    /// <summary>Added to the note of a compiled run whose coordinate system could not be converted, so its model was not read.</summary>
    public const string ModelCrsUnknown =
        " The survey's coordinate system is one this server cannot convert, so its model was stored but not read into stations.";

    /// <summary>
    /// The sentence an artefact entry carries beside its code, or null for a code that is not an
    /// artefact's. Fixed per code: nothing out of the survey goes into it.
    /// </summary>
    public static string? ForArtefact(string code) => code switch
    {
        SurveyPipelineFailures.OutputMissing => "Asked for, but Therion did not write it.",
        SurveyPipelineFailures.CrsUnknown =>
            "Stored, not read into stations: the survey's coordinate system is one this server cannot convert.",
        _ => null,
    };

    /// <summary>
    /// The note of a run that compiled: how many of the outputs asked for the compiler wrote, and
    /// whether the survey's sketches were drawn.
    /// </summary>
    public static string Compiled(int produced, int asked, bool sketchesDrawn) =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"Compiled: {produced} of {asked} outputs produced and stored.")
        + (sketchesDrawn ? "" : " No sketch was uploaded with the survey, so its drawings show the centreline only.");
}

/// <summary>What a compile came to, by the rule every compiler is judged by.</summary>
public enum SurveyCompileVerdict
{
    /// <summary>The compiler finished, its log does not say it failed, and at least one output asked for exists.</summary>
    Succeeded,

    /// <summary>The compiler exited with an error, or its log says the compile failed.</summary>
    CompilerFailed,

    /// <summary>The compiler said it finished, but nothing that was asked for was written.</summary>
    NothingProduced,
}

/// <summary>
/// Whether a compiled upload counts as compiled. An output asked for and not written is a gap in
/// that output alone — a KML of a survey that has no position is the ordinary case — so a run
/// fails only when the compiler did, or when none of what was asked for came out.
/// </summary>
public static class SurveyCompileOutcome
{
    public static SurveyCompileVerdict Judge(int exitCode, bool logSaysFailed, int producedCount) =>
        exitCode != 0 || logSaysFailed ? SurveyCompileVerdict.CompilerFailed
        : producedCount == 0 ? SurveyCompileVerdict.NothingProduced
        : SurveyCompileVerdict.Succeeded;
}
