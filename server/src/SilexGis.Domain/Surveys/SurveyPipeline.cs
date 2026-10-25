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
        _ => throw new ArgumentOutOfRangeException(nameof(stage), stage, null),
    };

    /// <summary>
    /// The run's percentage when a stage is <paramref name="percentOfStage"/> of the way through.
    /// A stage's own figure outside 0–100 is brought inside it, so a misreporting step can never
    /// move the bar backwards past its stage or forwards past the next.
    /// </summary>
    public static int Overall(SurveyUploadStage stage, int percentOfStage)
    {
        var (start, end) = Band(stage);
        var within = Math.Clamp(percentOfStage, 0, 100);
        return start + ((end - start) * within / 100);
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
