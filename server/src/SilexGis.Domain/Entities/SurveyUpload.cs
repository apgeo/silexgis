// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// Where one survey upload stands. Stored as smallint; values are part of the schema contract —
/// append, never renumber.
/// </summary>
public enum SurveyUploadStatus : short
{
    /// <summary>Opened; the device is still sending the files it declared.</summary>
    Receiving = 0,

    /// <summary>Every file arrived and was archived; the processing run is waiting for a worker.</summary>
    Queued = 1,

    /// <summary>A worker has it. <see cref="SurveyUpload.Stage"/> says where it has got to.</summary>
    Running = 2,

    /// <summary>The run finished. What it produced is listed beside the upload.</summary>
    Succeeded = 3,

    /// <summary>
    /// The run stopped. <see cref="SurveyUpload.ErrorCode"/> says why in a form a screen can
    /// translate. The archived files are untouched — they were archived before anything read them.
    /// </summary>
    Failed = 4,

    /// <summary>The device gave up, or the session expired before every file arrived.</summary>
    Abandoned = 5,
}

/// <summary>
/// The step of the processing run an upload is in, in the order they happen. Stored as smallint;
/// values are part of the schema contract — append, never renumber.
/// </summary>
public enum SurveyUploadStage : short
{
    /// <summary>Checking every declared file was archived when it arrived.</summary>
    Archive = 0,

    /// <summary>Reading the survey archive's manifest and metadata, on a copy.</summary>
    Unpack = 1,

    /// <summary>Building the project the compiler is run on around the survey's sources.</summary>
    Scaffold = 2,

    /// <summary>Running the survey compiler.</summary>
    Compile = 3,

    /// <summary>Storing what the compiler produced beside the archived files.</summary>
    Store = 4,

    /// <summary>Handing the stored models to the readers that index them.</summary>
    Read = 5,
}

/// <summary>
/// One upload of a survey from a device: a version of a <see cref="CaveSurvey"/>, the files that
/// came with it, and the status row its processing run writes as it goes.
/// </summary>
/// <remarks>
/// <para>
/// Every file is archived the moment it arrives — as a survey source of the cave, or as a document
/// of it — inside this upload's <see cref="UploadBatchId"/>, before anything reads it. What the
/// processing run reads is a copy; nothing it does can change what was archived.
/// </para>
/// <para>
/// The progress columns are written by the worker through direct update statements rather than
/// tracked saves, because the row is audited and a run's percentage chatter is not history. The
/// interceptor that keeps <see cref="UpdatedAt"/> never sees such an update, so every one of them
/// sets it itself. The database holds <see cref="Progress"/> to 0–100.
/// </para>
/// <para>
/// The row carries no rights of its own and is withheld exactly as its cave's survey sources are:
/// the message and the log tail are text the compiler produced over the caver's own files, and
/// station names and fixed coordinates can appear in them.
/// </para>
/// </remarks>
public class SurveyUpload : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The cave the upload was archived against (FK to the cave subtype row).</summary>
    public Guid CaveFeatureId { get; set; }

    /// <summary>The survey this upload is a version of.</summary>
    public Guid CaveSurveyId { get; set; }

    /// <summary>
    /// The upload batch every document this upload archived carries — which is what lets the whole
    /// upload be found, and taken back, as one act.
    /// </summary>
    public Guid UploadBatchId { get; set; }

    public SurveyOrigin Source { get; set; }

    /// <summary>The sending application's own name for itself, kept for the record and never trusted.</summary>
    public string? ClientName { get; set; }

    /// <summary>The sending application's version, kept for the record and never trusted.</summary>
    public string? ClientVersion { get; set; }

    /// <summary>The survey's title as this upload sent it.</summary>
    public required string SurveyTitle { get; set; }

    /// <summary>The tool's own name for the survey, as this upload sent it.</summary>
    public string? SurveyName { get; set; }

    public string? Description { get; set; }

    /// <summary>
    /// The version this upload is of its survey: the moment it was opened, to the whole second, and
    /// always later than the survey's previous version. Every artefact name carries it, so two
    /// versions of one survey never share one; the database holds it unique per survey.
    /// </summary>
    public DateTimeOffset VersionAt { get; set; }

    /// <summary>Whether this upload made its cave, because it named none. Kept so taking the upload back can say so.</summary>
    public bool CreatedCave { get; set; }

    /// <summary>
    /// Who the documents this upload makes are open to, when the request said; null means the
    /// cave's own visibility.
    /// </summary>
    public Visibility? DocumentsVisibility { get; set; }

    /// <summary>
    /// What the processing run was asked to do — compile or not, the outputs, the map scale — as a
    /// JSON object, so a rerun can repeat it.
    /// </summary>
    public string Options { get; set; } = "{}";

    /// <summary>The survey's date as the archive states it, read at unpacking; for the record.</summary>
    public DateOnly? SurveyDate { get; set; }

    /// <summary>
    /// The survey party as the archive states it, read at unpacking. Personal data of the people
    /// named: served only to readers of the cave's survey, never on a surface open to anyone.
    /// </summary>
    public string? Team { get; set; }

    /// <summary>The magnetic declination as the archive states it, in degrees; for the record.</summary>
    public double? Declination { get; set; }

    public SurveyUploadStatus Status { get; set; } = SurveyUploadStatus.Receiving;

    /// <summary>The step the run is in, or reached last; null before a run has started.</summary>
    public SurveyUploadStage? Stage { get; set; }

    /// <summary>0–100. Bounded by the database, not by trust.</summary>
    public int Progress { get; set; }

    /// <summary>What the run is doing now, in words, for the device's progress dialog.</summary>
    public string? Message { get; set; }

    /// <summary>A stable code for why the run failed, for a screen to translate.</summary>
    public string? ErrorCode { get; set; }

    /// <summary>Why the run failed, in words.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>The last lines of the compiler's output, for readers allowed to write to the cave.</summary>
    public string? LogTail { get; set; }

    /// <summary>The compiler the run used and its version, as the run reported it.</summary>
    public string? CompilerVersion { get; set; }

    /// <summary>
    /// Why the upload has no public link, as a stable code, when it was not given one at
    /// completion or its link was withdrawn; null while it has one, or before completion.
    /// </summary>
    public string? PublicLinkRefusal { get; set; }

    /// <summary>
    /// Whether the upload asked for a public link of its own accord: true or false overrides the
    /// account's preference for this upload alone; null leaves it to the preference.
    /// </summary>
    public bool? PublicLinkWanted { get; set; }

    /// <summary>Who the upload asked its link to open for; null means the account's default audience.</summary>
    public SurveyUploadLinkAudience? PublicLinkAudience { get; set; }

    /// <summary>
    /// Whether the uploader, warned that the results show where the cave's passages are, asked for
    /// a link even if the cave's position is protected. Never implied by any switch or preference;
    /// recorded on the link it makes as the uploader's acknowledgement at the time of opening.
    /// </summary>
    public bool PublicLinkAllowProtected { get; set; }

    /// <summary>The account that opened the upload.</summary>
    public Guid RequestedBy { get; set; }

    /// <summary>When a session still receiving files is abandoned by the sweep.</summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>When the first run started; kept across reruns.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When the last run finished, successfully or not.</summary>
    public DateTimeOffset? FinishedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // Uploads surface in their cave's timeline (features audit as "Feature").
    public string RootEntityType => nameof(Feature);

    public string RootEntityId => CaveFeatureId.ToString();
}

/// <summary>
/// What a file of a survey upload is. Stored as smallint; values are part of the schema contract —
/// append, never renumber.
/// </summary>
public enum SurveyUploadFileRole : short
{
    /// <summary>The tool's own archive of the survey — exactly one per upload.</summary>
    Archive = 0,

    /// <summary>A Therion survey source (<c>.th</c>).</summary>
    TherionSource = 1,

    /// <summary>A Therion sketch (<c>.th2</c>).</summary>
    TherionSketch = 2,

    /// <summary>Any other export the device sent along.</summary>
    Other = 3,

    /// <summary>The compiler configuration the run wrote around the sources.</summary>
    GeneratedConfig = 4,

    /// <summary>Something the compiler produced.</summary>
    Output = 5,

    /// <summary>The compiler's log.</summary>
    Log = 6,
}

/// <summary>
/// What became of one declared file. Stored as smallint; values are part of the schema contract —
/// append, never renumber.
/// </summary>
public enum SurveyUploadFileStatus : short
{
    /// <summary>Named when the upload was opened; nothing has arrived yet.</summary>
    Declared = 0,

    /// <summary>The bytes arrived and were checked.</summary>
    Received = 1,

    /// <summary>Archived as a survey source or a document of the cave.</summary>
    Stored = 2,

    /// <summary>Refused; <see cref="SurveyUploadFile.RefusalCode"/> says why.</summary>
    Refused = 3,
}

/// <summary>One file of a survey upload: what the device declared, and what became of it.</summary>
/// <remarks>
/// The archived document, source and model are referenced without holding them: each of those can
/// be deleted on its own terms, and this row then simply stops pointing at it rather than standing
/// in the way.
/// </remarks>
public class SurveyUploadFile : ITimestamped
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid SurveyUploadId { get; set; }

    /// <summary>The file name the device declared — unique within the upload, with no path in it.</summary>
    public required string Name { get; set; }

    public SurveyUploadFileRole Role { get; set; }

    public SurveyUploadFileStatus Status { get; set; } = SurveyUploadFileStatus.Declared;

    /// <summary>The size the device declared; a body of any other length is refused.</summary>
    public long DeclaredSize { get; set; }

    public long? ReceivedSize { get; set; }

    /// <summary>SHA-256 of what arrived, lower-case hex.</summary>
    public string? Sha256 { get; set; }

    /// <summary>The stable code a refused file was refused with.</summary>
    public string? RefusalCode { get; set; }

    public Guid? DocumentId { get; set; }

    public Guid? SurveySourceId { get; set; }

    public Guid? SurveyModelId { get; set; }

    /// <summary>
    /// For something the compiler produced, the output's catalogue name (<c>lox</c>,
    /// <c>plan-pdf</c>, …); null for every other file.
    /// </summary>
    public string? Output { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
