// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Entities;

/// <summary>
/// How far this application has got with an archived compilation log.
/// </summary>
/// <remarks>
/// Stored as a smallint and never renumbered: the value is part of the schema contract, so a new
/// member takes the next free number and nothing is reused. This says what happened when the log
/// was <em>read here</em>, which is a different question from how the compilation itself ended —
/// that answer is <see cref="SurveyCompilation.Outcome"/>, and a log this application could not
/// open has no answer to it at all.
/// </remarks>
public enum SurveyCompilationStatus : short
{
    /// <summary>Archived and queued; nothing has been read out of it yet.</summary>
    Pending = 0,

    /// <summary>Read. The figures on the row are what the log said.</summary>
    Read = 1,

    /// <summary>
    /// Could not be read — the file is not a compilation log, or reading it failed. Not the same as
    /// a compilation that failed: that one is a log this application read perfectly well, which
    /// happens to record a run that did not finish.
    /// </summary>
    Unreadable = 2,
}

/// <summary>
/// What one archived compilation log says about the survey it was written for: how the run ended,
/// what it ran as, and how well each of the survey's loops closed.
///
/// <para>
/// One row per archived log, and the figures on it are evidence of a single compilation rather than
/// a property of the survey. The same survey compiled again — at a different compiler version,
/// after a correction — produces different numbers, so the row carries which bytes it read
/// (<see cref="LogFileId"/>), which revision of the archived source those bytes were
/// (<see cref="LogVersionNumber"/>) and when they were read (<see cref="ReadAt"/>). A log carries
/// no timestamp of its own, so when the reading happened here is the only date there is; the
/// compiler's own release date is a fact about the compiler, not about the run.
/// </para>
///
/// <para>
/// The row carries no rights of its own. It belongs to a cave through the archived source it was
/// read from, and it is withheld on exactly the terms that source is: how well a survey closes is
/// not itself a position, but it is only ever readable through a record that names stations inside
/// a cave whose location may be protected, and it inherits that rather than being given a looser
/// rule of its own.
/// </para>
/// </summary>
public class SurveyCompilation : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The owning cave's feature id (FK to the cave subtype row).</summary>
    public Guid CaveFeatureId { get; set; }

    /// <summary>The archived source this log is. One compilation record per archived log.</summary>
    public Guid SurveySourceId { get; set; }

    /// <summary>
    /// The stored file whose bytes were read. Recorded without a foreign key on purpose: this is
    /// provenance, and a figure has to keep saying which file it came from even after that file has
    /// been superseded or purged. A constraint here would either block the purge or erase the
    /// answer, and both of those lose the thing the column exists to record.
    /// </summary>
    public Guid LogFileId { get; set; }

    /// <summary>
    /// Which revision of the archived source those bytes were. A corrected log replaces the figures
    /// of the run before it, and this is what says which run is being shown.
    /// </summary>
    public int LogVersionNumber { get; set; }

    /// <summary>How far this application has got with the log.</summary>
    public SurveyCompilationStatus Status { get; set; } = SurveyCompilationStatus.Pending;

    /// <summary>Why the log could not be read; null unless <see cref="Status"/> is unreadable.</summary>
    public string? ReadError { get; set; }

    /// <summary>When the log was read here; null until it has been.</summary>
    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>
    /// How the compilation ended, as the log reports it; null until the log has been read. A failed
    /// compilation is a quality signal of its own and is recorded as one — it is not a survey with
    /// no loops, and the two are never written the same way.
    /// </summary>
    public SurveyCompilationOutcome? Outcome { get; set; }

    /// <summary>The compiler's version string, as it announced itself.</summary>
    public string? CompilerVersion { get; set; }

    /// <summary>The release date the compiler announced for itself. Not when the run happened.</summary>
    public string? CompilerReleaseDate { get; set; }

    /// <summary>The step a failed run died in; null for a run that did not fail.</summary>
    public string? IncompleteStage { get; set; }

    /// <summary>How long the compiler said the run took, in seconds; null when it never said.</summary>
    public int? CompilationSeconds { get; set; }

    /// <summary>
    /// How many errors the compiler reported; null until the log has been read. Nullable for the
    /// same reason the figures beside it are: a log nobody could open reported no errors in the
    /// sense of having said nothing, which is not the same claim as a run that reported none.
    /// </summary>
    public int? ErrorCount { get; set; }

    /// <summary>How many warnings the compiler reported; null until the log has been read.</summary>
    public int? WarningCount { get; set; }

    /// <summary>
    /// How many loops the compiler said the survey has. Not the same number as the count of
    /// <see cref="Loops"/>: the count and the table are printed by different stages, so a run that
    /// stopped in between reports the count and no rows.
    /// </summary>
    public int? LoopCount { get; set; }

    /// <summary>The average relative loop error the compiler reported, as a percentage.</summary>
    public double? AverageLoopErrorPercent { get; set; }

    /// <summary>The total surveyed length the compiler reported, in metres.</summary>
    public double? TotalLengthM { get; set; }

    /// <summary>
    /// The total length after the compiler distributed the loop errors around the network, in
    /// metres. Its difference from <see cref="TotalLengthM"/> is how much the adjustment moved.
    /// </summary>
    public double? TotalLengthAdjustedM { get; set; }

    /// <summary>The loop-error table, in the order the compiler printed it.</summary>
    public List<SurveyCompilationLoop> Loops { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Compilation records surface in their cave's timeline (features audit as "Feature").
    public string RootEntityType => nameof(Feature);

    public string RootEntityId => CaveFeatureId.ToString();
}

/// <summary>
/// One row of the compiler's own loop-error table: how far a closed loop failed to close.
///
/// <para>
/// Both error measures are stored, never one. The absolute error is a <em>distance</em> — how far
/// apart the two arrivals at the closing station are. The relative error is a <em>ratio</em> — that
/// distance as a percentage of the loop's own length, which is what says whether the distance is
/// large for a loop of this size. A short loop can be much the worse ratio while being much the
/// smaller distance, so a reader shown one alone is told the wrong thing about half the loops, and
/// nothing ranks by one while labelling it the other.
/// </para>
///
/// <para>
/// The vocabulary is the compiler's, deliberately: its table prints REL-ERR, ABS-ERR, TOTAL-L, STS
/// and the three axis errors, so a figure here and a figure in the operator's own log are
/// recognisably the same number. Lengths are metres; the relative error is a percentage, so 71.92
/// means 71.92 %, not 0.7192.
/// </para>
/// </summary>
public class SurveyCompilationLoop
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SurveyCompilationId { get; set; }

    /// <summary>
    /// The loop's position in the compiler's own table, from zero. The rows carry no identity of
    /// their own — two loops can agree in every printed number and differ only in which stations
    /// they run through — so the order the compiler printed them in is what is preserved.
    /// </summary>
    public int Ordinal { get; set; }

    /// <summary>REL-ERR: the closing distance as a percentage of the loop's length.</summary>
    public double RelativeErrorPercent { get; set; }

    /// <summary>ABS-ERR: the closing distance itself, in metres.</summary>
    public double AbsoluteErrorM { get; set; }

    /// <summary>TOTAL-L: the length of the loop, in metres.</summary>
    public double TotalLengthM { get; set; }

    /// <summary>STS: how many stations the loop runs through.</summary>
    public int StationCount { get; set; }

    /// <summary>X-ERROR: the east–west component of the closing distance, in metres.</summary>
    public double ErrorXM { get; set; }

    /// <summary>Y-ERROR: the north–south component of the closing distance, in metres.</summary>
    public double ErrorYM { get; set; }

    /// <summary>Z-ERROR: the vertical component of the closing distance, in metres.</summary>
    public double ErrorZM { get; set; }

    /// <summary>
    /// The loop's station sequence exactly as the compiler printed it — a chain of survey-qualified
    /// station names joined by " - ". Kept as one string because it is the compiler's own text and
    /// nothing here can re-derive it. It names stations inside a survey, so it travels only where
    /// the survey itself travels.
    /// </summary>
    public required string Stations { get; set; }
}
