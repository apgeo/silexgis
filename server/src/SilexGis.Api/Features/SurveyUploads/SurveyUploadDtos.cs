// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;
using SilexGis.Domain.Surveys;
using SilexGis.Infrastructure.Scans;

namespace SilexGis.Api.Features.SurveyUploads;

/// <summary>What this server takes from a survey tool, read by the device before it uploads.</summary>
/// <param name="ContractVersion">The version of this exchange the server serves; a device sends it back as <c>X-SilexGis-Contract</c>.</param>
/// <param name="Sources">The survey tools accepted, by the names an upload's <c>source</c> uses.</param>
/// <param name="MaxFileBytes">The largest single file accepted, in bytes.</param>
/// <param name="MaxFilesPerUpload">The most files one upload may declare.</param>
/// <param name="AcceptedExtensions">Every file extension an upload may carry, lower-case with the dot.</param>
/// <param name="Compiler">Whether this server compiles surveys, and what it can produce.</param>
/// <param name="CaveCreation">Whether an upload that names no cave may make one, and how.</param>
/// <param name="Outputs">The output catalogue and what a run produces when an upload asks for nothing.</param>
/// <param name="PollIntervalSeconds">How often a device should ask how its upload is getting on.</param>
/// <param name="PublicLinks">What the calling account's uploads get by way of a result link when the upload does not say.</param>
/// <param name="PointCloud">What an upload of origin <c>pointcloud</c> may send: its own cap and extensions, apart from a survey tool's.</param>
public sealed record SurveyUploadCapabilitiesDto(
    int ContractVersion,
    IReadOnlyList<string> Sources,
    long MaxFileBytes,
    int MaxFilesPerUpload,
    IReadOnlyList<string> AcceptedExtensions,
    SurveyCompilerCapabilitiesDto Compiler,
    SurveyCaveCreationCapabilitiesDto CaveCreation,
    SurveyOutputCapabilitiesDto Outputs,
    int PollIntervalSeconds,
    SurveyPublicLinkCapabilitiesDto PublicLinks,
    SurveyPointCloudCapabilitiesDto PointCloud);

/// <param name="MaxFileBytes">The largest point cloud file an upload may send, in bytes.</param>
/// <param name="MaxFilesPerUpload">How many files a point-cloud upload declares: one, the cloud itself.</param>
/// <param name="AcceptedExtensions">The extensions a point cloud may have, lower-case with the dot.</param>
/// <param name="Recording">What an upload of origin <c>mandeye</c>, a scanner's raw recording folder, may send.</param>
public sealed record SurveyPointCloudCapabilitiesDto(
    long MaxFileBytes, int MaxFilesPerUpload, IReadOnlyList<string> AcceptedExtensions, SurveyRecordingCapabilitiesDto Recording);

/// <summary>
/// The limits of a raw recording upload. Every file of the folder is declared, by its bare name,
/// as the archive; a name the recording does not hold (a camera folder's pictures, for one) is
/// refused, so a device leaves those out.
/// </summary>
/// <param name="MaxFileBytes">The largest single file of the recording, in bytes.</param>
/// <param name="MaxTotalBytes">The recording's files together, at most, in bytes.</param>
/// <param name="MaxFiles">How many files a recording may declare.</param>
/// <param name="AcceptedExtensions">The extensions a recording's files may have, lower-case with the dot.</param>
/// <param name="OdometryWorkerFactor">
/// How many times the recording's size its odometry needs free on the processing worker's disk
/// while it runs, so a screen can ask about that room before the upload starts.
/// </param>
public sealed record SurveyRecordingCapabilitiesDto(
    long MaxFileBytes, long MaxTotalBytes, int MaxFiles, IReadOnlyList<string> AcceptedExtensions, int OdometryWorkerFactor);

/// <param name="Enabled">
/// Whether an upload of the calling account gets a link when it does not ask either way: the
/// installation makes links and the account has not turned them off. A protected cave still gets
/// none without the upload's explicit acknowledgement.
/// </param>
/// <param name="DefaultAudience">
/// Who such a link opens for when the upload names no audience — the account's default:
/// <c>public</c> (anybody holding the address) or <c>organization</c> (a signed-in account of this
/// installation).
/// </param>
public sealed record SurveyPublicLinkCapabilitiesDto(bool Enabled, SurveyUploadLinkAudience DefaultAudience);

/// <param name="Available">False when the server keeps the files but compiles nothing.</param>
/// <param name="Version">The compiler and its version, when one is configured.</param>
/// <param name="Outputs">The outputs the configured compiler can produce; empty when none is configured.</param>
public sealed record SurveyCompilerCapabilitiesDto(bool Available, string? Version, IReadOnlyList<string> Outputs);

/// <param name="Allowed">Whether an upload naming no cave may make one.</param>
/// <param name="DefaultCaveTypeId">The cave type such a cave gets when the upload names none.</param>
/// <param name="Visibilities">The visibilities such a cave may be given.</param>
public sealed record SurveyCaveCreationCapabilitiesDto(bool Allowed, long? DefaultCaveTypeId, IReadOnlyList<Visibility> Visibilities);

/// <param name="Catalogue">Every output a run can be asked for.</param>
/// <param name="Defaults">What a run produces when the upload asks for nothing.</param>
public sealed record SurveyOutputCapabilitiesDto(IReadOnlyList<string> Catalogue, IReadOnlyList<string> Defaults);

/// <summary>The sending application, for the record. Never trusted for anything.</summary>
public sealed record SurveyUploadClientInfo(string? Name, string? Version);

/// <summary>An existing cave, by id.</summary>
public sealed record SurveyUploadCaveReference(Guid Id);

/// <summary>An existing survey of a cave, by the id the server answered for it before.</summary>
public sealed record SurveyUploadCaveSurveyReference(Guid Id);

/// <summary>A cave to make for this upload.</summary>
/// <param name="Name">The cave's name; the survey's title when empty.</param>
/// <param name="Visibility">Who the cave is open to; the uploader's club, else private, when empty.</param>
/// <param name="CavingGroupId">The caving group to bind the cave to; needed for a group-visible cave when the account is in several.</param>
/// <param name="CaveTypeId">The cave type; the installation's default when empty.</param>
/// <param name="LocationProtected">
/// Whether the cave's position is protected from the moment it exists — before the entrance the
/// survey's fixed station places is written. Off when not sent, as on the web's form.
/// </param>
public sealed record SurveyUploadNewCave(
    string? Name, Visibility? Visibility, Guid? CavingGroupId, long? CaveTypeId, bool LocationProtected = false);

/// <summary>One file the device will send.</summary>
/// <param name="Name">The file name, unique within the upload, with no path.</param>
/// <param name="Size">The exact size in bytes the body will have.</param>
/// <param name="Role">What the file is.</param>
public sealed record SurveyUploadFileDeclaration(string Name, long Size, SurveyUploadFileRole Role);

/// <summary>What the processing run is asked to do.</summary>
/// <param name="Compile">Whether to compile the survey's Therion sources; true when empty.</param>
/// <param name="Outputs">Catalogue names; the installation's defaults when empty.</param>
/// <param name="MapScale">The map scale as 1:<c>MapScale</c>; 500 when empty.</param>
public sealed record SurveyUploadPipelineRequest(bool? Compile, IReadOnlyList<string>? Outputs, int? MapScale);

/// <summary>What one upload asks of its public link, overriding the account's preferences for it alone.</summary>
/// <param name="Wanted">
/// True asks for a link even when the account's preference makes none; false asks for none even when
/// it would. Empty leaves it to the preference.
/// </param>
/// <param name="Audience">Who the link opens for; the account's default audience when empty.</param>
/// <param name="AllowProtected">
/// The uploader's explicit acknowledgement, after being warned that the results show where the
/// cave's passages are, that a link may be made although the cave's position is protected. Without
/// it a protected cave gets no link. Off when not sent; no preference or switch stands in for it.
/// </param>
public sealed record SurveyUploadPublicLinkRequest(bool? Wanted, SurveyUploadLinkAudience? Audience, bool? AllowProtected);

/// <summary>Makes a new link by hand, from the web's status page or the device's result screen.</summary>
/// <param name="Audience">
/// Who the link opens for; when empty, the audience of the link it replaces, else the one the upload
/// asked for, else the caller's default audience.
/// </param>
/// <param name="AllowProtected">
/// The caller's explicit acknowledgement that the cave's position is protected and the results show
/// where its passages are. Without it a protected cave is refused a link.
/// </param>
public sealed record SurveyUploadPublicLinkMintRequest(SurveyUploadLinkAudience? Audience, bool? AllowProtected);

/// <summary>Opens an upload: says what is coming and where it goes.</summary>
/// <param name="Source">The survey tool, as named in the capabilities' <c>sources</c>.</param>
/// <param name="Client">The sending application, for the record.</param>
/// <param name="Cave">An existing cave — at most one of this, <paramref name="CaveSurvey"/> and <paramref name="NewCave"/>.</param>
/// <param name="CaveSurvey">An existing survey of a cave, by the id the server answered for it before.</param>
/// <param name="NewCave">A cave to make. With none of the three, one is made from the survey's title.</param>
/// <param name="SurveyTitle">The survey's title, which its artefacts are grouped under.</param>
/// <param name="SurveyName">The tool's own name for the survey, which a re-upload is recognised by.</param>
/// <param name="Description">A note for the record.</param>
/// <param name="Files">Every file that will be sent, the survey archive among them.</param>
/// <param name="Pipeline">What the processing run is asked to do.</param>
/// <param name="DocumentsVisibility">Who the documents this upload makes are open to; the cave's own visibility when empty.</param>
/// <param name="PublicLink">What this upload asks of its public link; the account's preferences decide when empty.</param>
/// <param name="Display">
/// A point cloud's choice of how much to show, decided ahead of the estimate. Empty, the run
/// tiles a cloud under the installation's thresholds and stops at the estimate for one over
/// them, waiting for a rerun that chooses. Ignored for a survey tool's upload.
/// </param>
/// <param name="Profiles">
/// A point cloud's processing profiles, at most one per step: each one's values are laid over the
/// step's defaults and the upload stores the result, never the profile. A step no profile is named
/// for runs with its defaults. Each one named is remembered as the caller's last choice for its
/// step. Ignored for a survey tool's upload.
/// </param>
/// <param name="ExternalSteps">
/// A scan's steps, by id, that this upload has done on another computer instead of by the
/// server's worker — such as <c>hdmapping.odometry</c> for a recording whose odometry is run on
/// a desktop. The run stops before each with its inputs offered as a download and carries on
/// when the step's outputs are uploaded. Each must be a step of the upload's recipe that a
/// worker would otherwise run. Refused for a survey tool's upload.
/// </param>
public sealed record SurveyUploadOpenRequest(
    string Source,
    SurveyUploadClientInfo? Client,
    SurveyUploadCaveReference? Cave,
    SurveyUploadCaveSurveyReference? CaveSurvey,
    SurveyUploadNewCave? NewCave,
    string? SurveyTitle,
    string? SurveyName,
    string? Description,
    IReadOnlyList<SurveyUploadFileDeclaration> Files,
    SurveyUploadPipelineRequest? Pipeline,
    Visibility? DocumentsVisibility,
    SurveyUploadPublicLinkRequest? PublicLink = null,
    SurveyUploadDisplayRequest? Display = null,
    IReadOnlyList<Guid>? Profiles = null,
    IReadOnlyList<string>? ExternalSteps = null);

/// <summary>Runs an upload's processing again, with the options it ran with or with new ones.</summary>
/// <param name="Pipeline">A survey archive's options; ignored for a point cloud.</param>
/// <param name="Display">A point cloud's choice of how much to show: the recipe is rebuilt around it. Null keeps the recipe as it is, thinning included.</param>
/// <param name="FromStep">A point cloud's step to do again even where an earlier run's product would do; the steps after it follow. Applies to this rerun only.</param>
/// <param name="Profiles">A point cloud's processing profiles for this run, at most one per step, resolved into the recipe as with an upload; the steps no profile is named for keep the values they had.</param>
public sealed record SurveyUploadRerunRequest(
    SurveyUploadPipelineRequest? Pipeline,
    SurveyUploadDisplayRequest? Display = null,
    string? FromStep = null,
    IReadOnlyList<Guid>? Profiles = null);

/// <summary>How much of a point cloud to show: <c>full</c>, one point per <c>voxel</c> of <c>voxelCm</c>, or <c>everyNth</c> point.</summary>
public sealed record SurveyUploadDisplayRequest(string Method, double? VoxelCm = null, int? EveryNth = null);

/// <summary>The rules a display choice meets wherever it is sent — with the upload or with a rerun.</summary>
public sealed class SurveyUploadDisplayRequestValidator : AbstractValidator<SurveyUploadDisplayRequest>
{
    public SurveyUploadDisplayRequestValidator()
    {
        RuleFor(x => x.Method).Must(m => PointCloudDisplayChoice.Methods.Contains(m))
            .WithMessage("The display method must be one of: full, voxel, everyNth.");
        RuleFor(x => x.VoxelCm).ExclusiveBetween(0, 1000).When(x => x.VoxelCm is not null);
        RuleFor(x => x.EveryNth).InclusiveBetween(1, 10000).When(x => x.EveryNth is not null);
    }
}

/// <summary>The cave an upload belongs to, as the caller may see it.</summary>
/// <param name="Url">The cave's page, relative to the installation's public address.</param>
public sealed record SurveyUploadCaveDto(Guid Id, string Name, string Url);

/// <summary>The survey an upload is a version of.</summary>
public sealed record SurveyUploadCaveSurveyDto(Guid Id, string Title);

/// <summary>What the archive says about the survey, read when it is unpacked.</summary>
/// <param name="Title">The survey's title as this upload sent it.</param>
/// <param name="Name">The tool's own name for the survey.</param>
/// <param name="Date">The survey's date, once read from the archive.</param>
/// <param name="Team">The survey party, once read from the archive.</param>
public sealed record SurveyUploadSurveyDto(string Title, string? Name, DateOnly? Date, string? Team);

/// <summary>The coordinate system a point cloud's file named, as it named it.</summary>
public sealed record SurveyUploadPointCloudCrsDto(int? HorizontalEpsg, int? VerticalEpsg, string? Wkt);

/// <summary>
/// What is known about an uploaded point cloud: its header's facts once the run has read them,
/// and what tiling added once a worker has tiled it. Null on an upload of a survey archive, and
/// on a point cloud whose header has not been read yet.
/// </summary>
/// <param name="ModelId">The model that shows the scan.</param>
/// <param name="PointCount">How many points the file holds.</param>
/// <param name="PointFormat">The LAS point record format, 0 to 10.</param>
/// <param name="LasVersion">The LAS version, such as <c>1.4</c>.</param>
/// <param name="Compressed">Whether the points are LAZ-compressed.</param>
/// <param name="HasRgb">Whether each point carries a colour.</param>
/// <param name="HasIntensity">Whether the points carry return intensities; null until a step that reads the points has answered.</param>
/// <param name="SizeM">The extent's size per axis, in the file's units: a measurement, shown to everybody who sees the scan.</param>
/// <param name="Georeferenced">Whether the file names a coordinate system — which makes its original a position, downloadable only with the exact-location right.</param>
/// <param name="Crs">The coordinate system named, with the exact-location right only.</param>
/// <param name="Min">The extent's lowest corner, with the exact-location right only.</param>
/// <param name="Max">The extent's highest corner, with the exact-location right only.</param>
/// <param name="GeneratingSoftware">What wrote the file, as it named itself.</param>
/// <param name="TileBytes">The tileset's size, once tiled.</param>
/// <param name="TileCount">How many tiles, once tiled.</param>
/// <param name="TilerVersion">The tiler's version, once tiled.</param>
/// <param name="TilesetUrl">A signed address of the tileset's root, good for a session, once tiled; the tiles are in the cloud's own frame and carry no coordinates, so it goes to everybody who may read the upload.</param>
/// <param name="DisplayVoxelM">The voxel the shown copy was thinned to, or null when the whole cloud was tiled.</param>
/// <param name="DisplayEveryNth">The stride the shown copy was thinned by, when every n-th point was kept.</param>
/// <param name="DisplayPointCount">How many points the shown copy holds, when it was thinned.</param>
/// <param name="OriginShift">What was subtracted from the file's coordinates before tiling; a position for a georeferenced file, so with the exact-location right only.</param>
/// <param name="Estimate">What a thinned copy would keep at each voxel size, once the worker has estimated it.</param>
/// <param name="Advice">The installation's advice on thinning, with the thresholds it was judged by.</param>
/// <param name="Recording">What reading a raw recording found, when the cloud came from one; measurements only, no position.</param>
/// <param name="SessionUrl">
/// A signed, short-lived address of the scan's HDMapping session — what the odometry made, or
/// the session sent back for it — for refining in HDMapping's desktop program and sending back
/// as the scan's next version. The session holds the scanner's path, so with the exact-location
/// right only; null without it, and for a scan that has no session.
/// </param>
public sealed record SurveyUploadPointCloudDto(
    Guid ModelId,
    long PointCount,
    int PointFormat,
    string LasVersion,
    bool Compressed,
    bool HasRgb,
    bool? HasIntensity,
    double[] SizeM,
    bool Georeferenced,
    SurveyUploadPointCloudCrsDto? Crs,
    double[]? Min,
    double[]? Max,
    string? GeneratingSoftware,
    long? TileBytes,
    int? TileCount,
    string? TilerVersion,
    string? TilesetUrl,
    double? DisplayVoxelM,
    int? DisplayEveryNth,
    long? DisplayPointCount,
    double[]? OriginShift,
    SurveyUploadPointCloudEstimateDto? Estimate,
    SurveyUploadPointCloudAdviceDto? Advice,
    SurveyUploadRecordingDto? Recording,
    string? SessionUrl = null);

/// <summary>What an inspection read of a raw scanner recording.</summary>
/// <param name="ChunkCount">How many lidar/IMU chunk pairs the recording holds.</param>
/// <param name="SensorCount">How many lidar sensors wrote it, or null when neither a calibration nor serial numbers said.</param>
/// <param name="HasCalibration">Whether it carries a calibration of its sensors.</param>
/// <param name="HasGnss">Whether a GNSS log holds anything; such a recording is treated as georeferenced.</param>
/// <param name="DurationSeconds">From the first to the last IMU sample, or null when the logs did not say.</param>
/// <param name="PointCount">The points of every lidar chunk together.</param>
/// <param name="ImuHasHeader">Whether the IMU logs open with a line naming their columns.</param>
/// <param name="Bytes">The recording's files together, in bytes.</param>
public sealed record SurveyUploadRecordingDto(
    int ChunkCount, int? SensorCount, bool HasCalibration, bool HasGnss, double? DurationSeconds, long PointCount, bool ImuHasHeader, long Bytes);

/// <summary>One row of the estimate: what tiling at this voxel size (null for the full cloud) would come to.</summary>
public sealed record SurveyUploadPointCloudEstimateRowDto(double? VoxelCm, long? Points, long? EstimatedTileBytes, int? EstimatedSeconds);

/// <summary>The estimate table: the full cloud's row and one row per voxel size counted.</summary>
public sealed record SurveyUploadPointCloudEstimateDto(
    SurveyUploadPointCloudEstimateRowDto Full, IReadOnlyList<SurveyUploadPointCloudEstimateRowDto> Voxels);

/// <summary>The installation's advice on thinning a cloud before it is shown.</summary>
/// <param name="Level"><c>none</c>, <c>warn</c> or <c>advise</c>.</param>
/// <param name="SuggestedVoxelCm">The voxel size pre-selected for the person, or null when none is needed.</param>
/// <param name="WarnPoints">The point count above which thinning is suggested.</param>
/// <param name="AdvisePoints">The point count above which thinning is strongly advised.</param>
/// <param name="WarnTileBytes">The estimated tile size above which thinning is suggested.</param>
public sealed record SurveyUploadPointCloudAdviceDto(string Level, double? SuggestedVoxelCm, long WarnPoints, long AdvisePoints, long WarnTileBytes);

/// <summary>Why a run failed, in a form a screen can translate and in words.</summary>
public sealed record SurveyUploadErrorDto(string Code, string? Message);

/// <summary>The compiler a run used, as the run reported it.</summary>
public sealed record SurveyUploadCompilerDto(string Version);

/// <summary>One file of an upload, and what became of it.</summary>
/// <param name="Name">The file name as declared.</param>
/// <param name="Role">What the file is.</param>
/// <param name="Status">Declared, received, stored or refused.</param>
/// <param name="DeclaredSize">The size the device declared.</param>
/// <param name="ReceivedSize">The size that arrived.</param>
/// <param name="Sha256">SHA-256 of what arrived, lower-case hex.</param>
/// <param name="RefusalCode">The code a refused file was refused with.</param>
/// <param name="SourceId">The survey source it was archived as, if it was one.</param>
/// <param name="DocumentId">The document it was archived as.</param>
/// <param name="ContentUrl">A signed, short-lived address of the archived bytes.</param>
public sealed record SurveyUploadFileDto(
    string Name,
    SurveyUploadFileRole Role,
    SurveyUploadFileStatus Status,
    long DeclaredSize,
    long? ReceivedSize,
    string? Sha256,
    string? RefusalCode,
    Guid? SourceId,
    Guid? DocumentId,
    string? ContentUrl);

/// <summary>Something a run produced.</summary>
/// <param name="Kind">The output's catalogue name, or <c>log</c>.</param>
/// <param name="Name">The file name, stamped with the upload's version.</param>
/// <param name="SizeBytes">The size of the stored file; null for an output that was not produced.</param>
/// <param name="ContentUrl">A signed, short-lived address of the bytes; null for an output that was not produced.</param>
/// <param name="SurveyModelId">The survey model it was stored as, for a model.</param>
/// <param name="DocumentId">The document it was stored as, for a drawing.</param>
/// <param name="SourceId">The survey source it was stored as, for the log.</param>
/// <param name="ViewUrl">A page to view it on, relative to the installation's public address.</param>
/// <param name="ModelStatus">
/// For a model, how far its reading has got: a run reports success before the model has been read
/// into stations, and this says when it has.
/// </param>
/// <param name="Error">
/// What is wrong with this one output, with a stable code: <c>survey_pipeline.output_missing</c>
/// for an output asked for and not produced (listed with no file), or
/// <c>survey_pipeline.crs_unknown</c> for a Therion model stored and not read because its
/// coordinate system cannot be converted. Null for an output stored as asked.
/// </param>
public sealed record SurveyUploadArtefactDto(
    string Kind,
    string Name,
    long? SizeBytes,
    string? ContentUrl,
    Guid? SurveyModelId,
    Guid? DocumentId,
    Guid? SourceId,
    string? ViewUrl,
    SurveyModelStatus? ModelStatus,
    SurveyUploadErrorDto? Error);

/// <summary>A public link to an upload's results.</summary>
/// <param name="Url">The page's address, relative to the installation's public address.</param>
/// <param name="CreatedAt">When the link was made.</param>
/// <param name="Audience">Who it opens for: anybody holding it, or a signed-in account of this installation.</param>
/// <param name="ProtectedAcknowledged">
/// Whether it was made with the explicit acknowledgement that the cave's position is protected — the
/// only way a protected cave's results are linked, and the reason such a link keeps opening.
/// </param>
public sealed record SurveyUploadPublicLinkDto(
    string Url, DateTimeOffset CreatedAt, SurveyUploadLinkAudience Audience, bool ProtectedAcknowledged);

/// <summary>An upload, and how its processing is getting on.</summary>
/// <param name="Id">The upload.</param>
/// <param name="Status">Where the upload stands.</param>
/// <param name="Stage">The step the run is in, or reached last.</param>
/// <param name="Progress">0–100.</param>
/// <param name="Message">What the run is doing now, in words.</param>
/// <param name="Error">Why the run failed, when it did.</param>
/// <param name="Cave">The cave the upload belongs to.</param>
/// <param name="CaveSurvey">The survey this upload is a version of — the id a device keeps to send the next version to.</param>
/// <param name="Version">This upload's version of its survey: its UTC moment, to the second.</param>
/// <param name="CaveCreated">Whether this upload made its cave.</param>
/// <param name="Survey">What the upload and its archive say about the survey.</param>
/// <param name="Files">Every declared file and what became of it.</param>
/// <param name="Artefacts">What the run produced so far.</param>
/// <param name="Compiler">The compiler the run used, once it has run.</param>
/// <param name="Outputs">The outputs the run was asked for, combined with the account's and the installation's.</param>
/// <param name="Compile">Whether the run was asked to compile.</param>
/// <param name="LogTail">The last lines of the compiler's output — only for callers who may write to the cave.</param>
/// <param name="StatusPageUrl">The signed-in page the run is watched on, relative to the installation's public address.</param>
/// <param name="PublicLink">
/// The public link to the results, when there is one that opens now. Shown to every caller who may
/// read the upload; only those who may write to the cave may withdraw it or make a new one.
/// </param>
/// <param name="PublicLinkRefusal">
/// Why the upload has no public link, when it was given none or its link no longer opens:
/// <c>installation-off</c>, <c>account-off</c>, <c>not-wanted</c>, <c>no-share-right</c>,
/// <c>protected-cave</c> or <c>revoked</c>; null otherwise.
/// </param>
/// <param name="OlderVersions">What the survey's older versions hold of their runs' outputs, and whether that warrants a warning.</param>
/// <param name="PointCloud">The scan's facts, for an upload of a point cloud whose header has been read.</param>
/// <param name="CreatedAt">When the upload was opened.</param>
/// <param name="ExpiresAt">When an upload still receiving files is abandoned.</param>
/// <param name="StartedAt">When the first run started.</param>
/// <param name="FinishedAt">When the last run finished.</param>
/// <param name="External">
/// The step a scan's run stopped before because it is done on another computer, and what to send
/// back for it; null when the run is not waiting for one, and for a caller who may not write to
/// the cave (the download and the upload back both need Write).
/// </param>
public sealed record SurveyUploadDto(
    Guid Id,
    SurveyUploadStatus Status,
    SurveyUploadStage? Stage,
    int Progress,
    string? Message,
    SurveyUploadErrorDto? Error,
    SurveyUploadCaveDto Cave,
    SurveyUploadCaveSurveyDto CaveSurvey,
    DateTimeOffset Version,
    bool CaveCreated,
    SurveyUploadSurveyDto Survey,
    SurveyUploadPointCloudDto? PointCloud,
    IReadOnlyList<SurveyUploadFileDto> Files,
    IReadOnlyList<SurveyUploadArtefactDto> Artefacts,
    SurveyUploadCompilerDto? Compiler,
    IReadOnlyList<string> Outputs,
    bool Compile,
    string? LogTail,
    string StatusPageUrl,
    SurveyUploadPublicLinkDto? PublicLink,
    string? PublicLinkRefusal,
    SurveyUploadOlderVersionsDto OlderVersions,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    SurveyUploadExternalWaitDto? External = null);

/// <summary>A scan's step done on another computer, which the run waits for.</summary>
/// <param name="StepId">The step's registry id, such as <c>hdmapping.odometry</c>.</param>
/// <param name="StepName">The step's name, in English, as the download's README gives it.</param>
/// <param name="Tool">The program that does the step, such as <c>hdmapping</c>.</param>
/// <param name="Outputs">Each product the step makes, the names it may be sent back under, and whether it has arrived.</param>
public sealed record SurveyUploadExternalWaitDto(
    string StepId,
    string StepName,
    string Tool,
    IReadOnlyList<SurveyUploadExternalOutputDto> Outputs);

/// <summary>One product of a step done on another computer.</summary>
/// <param name="Names">The file names it is accepted under; the first is the one to use.</param>
/// <param name="Received">Whether it has been sent back already.</param>
public sealed record SurveyUploadExternalOutputDto(IReadOnlyList<string> Names, bool Received);

/// <summary>
/// What the survey's versions older than its newest one still hold of what their runs produced —
/// the models, drawings and logs — and whether that is past the size the installation warns at.
/// The same answer on the status of every version of one survey.
/// </summary>
/// <param name="Count">Older versions that still hold something their run produced.</param>
/// <param name="SizeBytes">What those hold, in bytes: the current revision of each stored output and log.</param>
/// <param name="Warn">Whether <paramref name="SizeBytes"/> is past the installation's warning size.</param>
/// <param name="WarnBytes">The installation's warning size in bytes; null when it never warns.</param>
/// <param name="KeepVersions">How many newest versions of a survey keep what their runs produced; 0 keeps every version's.</param>
public sealed record SurveyUploadOlderVersionsDto(int Count, long SizeBytes, bool Warn, long? WarnBytes, int KeepVersions);

/// <summary>A file was received and archived — or had been already, for a repeated request.</summary>
/// <param name="Name">The file name.</param>
/// <param name="Status">Always <c>stored</c>: a file is archived the moment it arrives.</param>
/// <param name="Sha256">SHA-256 of what arrived, lower-case hex.</param>
/// <param name="ReceivedSize">The size that arrived.</param>
/// <param name="SourceId">The survey source it was archived as, if it was one.</param>
/// <param name="DocumentId">The document it was archived as.</param>
public sealed record SurveyUploadFileReceivedDto(
    string Name,
    SurveyUploadFileStatus Status,
    string Sha256,
    long ReceivedSize,
    Guid? SourceId,
    Guid? DocumentId);

/// <summary>A file of a listed upload the device can download.</summary>
public sealed record SurveyUploadListFileDto(string Name, long SizeBytes, string ContentUrl);

/// <summary>Who sent an upload, as the caller may see them.</summary>
public sealed record SurveyUploadUploaderDto(Guid Id, string DisplayName);

/// <summary>One upload in a listing.</summary>
/// <param name="CaveName">The cave's name, as the caller sees it.</param>
/// <param name="Archive">The survey archive, when it was archived and is still there.</param>
/// <param name="TherionSources">The Therion sources sent with it, importable on their own.</param>
/// <param name="PointCloud">The scan's facts, for an upload of a point cloud whose header has been read.</param>
/// <param name="PublicLink">The upload's public link, when it has one that opens now.</param>
public sealed record SurveyUploadListItemDto(
    Guid Id,
    Guid CaveId,
    string CaveName,
    SurveyUploadCaveSurveyDto CaveSurvey,
    DateTimeOffset Version,
    SurveyOrigin Source,
    string SurveyTitle,
    string? SurveyName,
    DateOnly? SurveyDate,
    SurveyUploadUploaderDto UploadedBy,
    SurveyUploadStatus Status,
    DateTimeOffset CreatedAt,
    SurveyUploadListFileDto? Archive,
    IReadOnlyList<SurveyUploadListFileDto> TherionSources,
    SurveyUploadPointCloudDto? PointCloud,
    string StatusPageUrl,
    SurveyUploadPublicLinkDto? PublicLink);

/// <summary>A page of uploads, newest first.</summary>
/// <param name="Total">How many uploads the caller may see under these filters — nothing beyond them.</param>
public sealed record SurveyUploadPageDto(IReadOnlyList<SurveyUploadListItemDto> Items, int Total, int Page, int PageSize);

public sealed class SurveyUploadOpenRequestValidator : AbstractValidator<SurveyUploadOpenRequest>
{
    public SurveyUploadOpenRequestValidator()
    {
        RuleFor(x => x.Source).NotEmpty().MaximumLength(50);
        RuleFor(x => x.SurveyTitle).MaximumLength(SurveyCaveNaming.MaxLength);
        RuleFor(x => x.SurveyName).MaximumLength(CaveSurveyMatching.MaxExternalKeyLength);
        RuleFor(x => x)
            .Must(x => SurveyCaveNaming.TitleOf(x.SurveyTitle, x.SurveyName) is not null)
            .OverridePropertyName("surveyTitle")
            .WithMessage("A survey title or a survey name is needed.");
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Client!.Name).MaximumLength(100).When(x => x.Client is not null);
        RuleFor(x => x.Client!.Version).MaximumLength(50).When(x => x.Client is not null);
        RuleFor(x => x.NewCave!.Name).MaximumLength(SurveyCaveNaming.MaxLength).When(x => x.NewCave is not null);

        // The serializer takes a visibility as a number too, and a number outside the four would
        // otherwise be stored on a cave or a document as a fifth visibility nobody defined.
        RuleFor(x => x.NewCave!.Visibility).IsInEnum().When(x => x.NewCave?.Visibility is not null);
        RuleFor(x => x.DocumentsVisibility).IsInEnum().When(x => x.DocumentsVisibility is not null);
        RuleFor(x => x.Files).NotNull();
        RuleForEach(x => x.Files).NotNull().WithMessage("A file declaration may not be empty.");
        RuleFor(x => x.Pipeline!).SetValidator(new SurveyUploadPipelineRequestValidator()).When(x => x.Pipeline is not null);
        RuleFor(x => x.PublicLink!.Audience).IsInEnum().When(x => x.PublicLink?.Audience is not null);
        RuleFor(x => x.Display!).SetValidator(new SurveyUploadDisplayRequestValidator()).When(x => x.Display is not null);
        RuleFor(x => x.Profiles!.Count).LessThanOrEqualTo(ProcessingProfileResolver.MaxProfiles)
            .OverridePropertyName("profiles").When(x => x.Profiles is not null);
        // A recipe holds a handful of steps; a longer list is not a choice anybody made.
        RuleFor(x => x.ExternalSteps!.Count).LessThanOrEqualTo(MaxExternalSteps)
            .OverridePropertyName("externalSteps").When(x => x.ExternalSteps is not null);
        RuleForEach(x => x.ExternalSteps).NotEmpty().MaximumLength(100).When(x => x.ExternalSteps is not null);
    }

    /// <summary>The most steps one upload may name as done on another computer.</summary>
    public const int MaxExternalSteps = 16;
}

public sealed class SurveyUploadPublicLinkMintRequestValidator : AbstractValidator<SurveyUploadPublicLinkMintRequest>
{
    public SurveyUploadPublicLinkMintRequestValidator() =>
        RuleFor(x => x.Audience).IsInEnum().When(x => x.Audience is not null);
}

public sealed class SurveyUploadRerunRequestValidator : AbstractValidator<SurveyUploadRerunRequest>
{
    public SurveyUploadRerunRequestValidator()
    {
        RuleFor(x => x.Pipeline!).SetValidator(new SurveyUploadPipelineRequestValidator()).When(x => x.Pipeline is not null);
        RuleFor(x => x.Display!).SetValidator(new SurveyUploadDisplayRequestValidator()).When(x => x.Display is not null);
        RuleFor(x => x.FromStep).Must(id => ScanSteps.Find(id!) is not null).When(x => x.FromStep is not null)
            .WithMessage("The step to run again from is not one this server knows.");
        RuleFor(x => x.Profiles!.Count).LessThanOrEqualTo(ProcessingProfileResolver.MaxProfiles)
            .OverridePropertyName("profiles").When(x => x.Profiles is not null);
    }
}

public sealed class SurveyUploadPipelineRequestValidator : AbstractValidator<SurveyUploadPipelineRequest>
{
    public SurveyUploadPipelineRequestValidator()
    {
        RuleFor(x => x.MapScale)
            .InclusiveBetween(SurveyUploadRunOptions.MinMapScale, SurveyUploadRunOptions.MaxMapScale)
            .When(x => x.MapScale is not null);
        RuleFor(x => x.Outputs!.Count)
            .LessThanOrEqualTo(SurveyOutputs.MaxCount)
            .OverridePropertyName("outputs")
            .When(x => x.Outputs is not null);
    }
}
