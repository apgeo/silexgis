// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// What happened to one uploaded row. A batch is answered row by row rather than as a single
/// verdict: a device that edited forty caves offline and lost one of them to a conflict has to
/// be told which one, and must not have the other thirty-nine thrown away with it.
/// </summary>
public enum SyncRowStatus
{
    /// <summary>The row did not exist here and now does, under the identifier the device gave it.</summary>
    Created = 0,

    /// <summary>The row existed, the device held the current version of it, and its edit was written.</summary>
    Updated = 1,

    /// <summary>The row was soft-deleted.</summary>
    Deleted = 2,

    /// <summary>
    /// Nothing was written and nothing was wrong: the row already said what the device asked for.
    /// A resent create whose identifier is already here lands on this, which is what makes a
    /// retry after a lost answer safe.
    /// </summary>
    Unchanged = 3,

    /// <summary>
    /// Somebody else wrote the row after the device last read it. The device's version was not
    /// applied; the server's stands.
    /// </summary>
    Conflict = 4,

    /// <summary>The row could not be written at all. <c>Code</c> says why.</summary>
    Rejected = 5,

    /// <summary>
    /// The account's uploads wait for a reviewer: nothing was written, and the row is kept as a
    /// proposal under <c>reviewId</c> until somebody decides it. The revision is the one the row
    /// already had. Only ever answered to a device that listed <c>held</c> in its
    /// <c>clientFeatures</c>.
    /// </summary>
    Held = 6,
}

/// <summary>
/// One row a device asks this server to write.
/// </summary>
/// <param name="Id">
/// The device's own identifier for the row, adopted verbatim as the feature identifier the first
/// time it arrives. Both sides mint version-7 uuids and neither ever re-keys the other's, which
/// is what removes the translation table that a two-sided identifier scheme would otherwise need
/// — and with it every way that table could go stale.
/// </param>
/// <param name="BaseRevision">
/// The server revision the device last saw for this row, or null to say the row is new. It is
/// compared for equality and nothing else: a device's own clock never enters the comparison,
/// because a phone's clock is unsynchronised, resettable by whoever holds it, and routinely wrong
/// by hours, so a clock-ordered merge would let a bad clock overwrite anything.
/// </param>
/// <param name="ParentId">
/// The row this one is contained by. Structure is not accepted as a free edge list: the server
/// makes the one containment edge itself, through the writer that maintains what hangs off it,
/// because protection and visibility are both inherited along containment and a client-declared
/// edge is a client-declared audience.
/// </param>
/// <param name="Deleted">Asks for the row to go. Arbitrated exactly like an edit.</param>
/// <param name="FeatureTypeCode">
/// The kind of a generic row, by its stable code — never by a numeric identifier, which is
/// assigned by whichever installation seeded the table and is not comparable between two servers.
/// </param>
/// <param name="ClientUpdatedAt">
/// When the device believes it last wrote this row, by its own clock. Stored as provenance so two
/// devices that were both offline can compare their versions with each other; never consulted here.
/// </param>
public sealed record SyncUploadRowDto(
    Guid Id,
    FeatureKind Kind,
    DateTimeOffset? BaseRevision,
    bool Deleted,
    Guid? ParentId,
    string? Name,
    string? Description,
    string? FeatureTypeCode,
    string? CaveTypeCode,
    string? EntranceTypeCode,
    bool IsMain,
    GeoJsonGeometry? Geometry,
    decimal? Altitude,
    PositionQuality? PositionQuality,
    JsonElement? Properties,
    DateTimeOffset? ClientUpdatedAt);

/// <summary>
/// One upload: the rows, in the order they depend on each other, under an identifier the device
/// minted for the attempt.
/// </summary>
/// <param name="BatchId">
/// The device's name for this attempt. Sending it again returns the first answer and writes
/// nothing, which is what makes an upload safe to retry from a car park with one bar of signal.
/// </param>
/// <param name="ContractVersion">
/// The protocol generation the device was built against. Stated on every write rather than
/// assumed, so a device meeting a server it does not understand is told so before anything is
/// written rather than after.
/// </param>
/// <param name="Rows">
/// The feature rows, in dependency order: a cave before its entrances, an area before the places
/// inside it. They are applied in the order given, so a row whose container is created earlier in
/// the same batch finds it. Optional since an upload can carry other sections instead; a request
/// must carry at least one item in some section.
/// </param>
/// <param name="ClientFeatures">
/// The protocol behaviours this device understands beyond the first contract, by name. A server
/// only ever answers in a way a device opted into here, so a build that predates a behaviour is
/// never handed an answer it would misread. Values this server does not recognise are kept and
/// otherwise ignored, exactly as a device ignores members of an answer it does not recognise.
/// </param>
/// <param name="People">
/// The phone's own users, as the phone describes them, each optionally linked to a caver the
/// people directory listed. Applied after the feature rows.
/// </param>
/// <param name="Documents">
/// The photos and documents the phone created, changed or removed, each naming the bytes it
/// staged beforehand through the resumable transfer routes when it carries new ones. Applied
/// after the phone users, and before anything that hangs a document on a feature.
/// </param>
/// <param name="DocumentLinks">
/// Where the phone hung its photos and documents, and where it took them down: each link names a
/// document and the object it hangs on, and is applied after the documents, so a link may name a
/// document sent in the same upload.
/// </param>
/// <param name="TripRecordings">
/// The phone's recordings of trips, each arbitrated on its own. Applied after the phone users.
/// </param>
/// <param name="TripPoints">
/// The scans on those recordings, applied after the recordings so a scan sent beside its new
/// recording finds it.
/// </param>
/// <param name="RasterMaps">
/// The phone's raster maps, each with its picture staged beforehand. Applied after the documents.
/// </param>
/// <param name="RasterPins">
/// The places pinned on those maps, applied last so a pin sent beside its new map and a place sent
/// beside it finds both.
/// </param>
/// <remarks>
/// Each array is a section, applied in a fixed order whatever order the arrays sit in here —
/// feature rows first — and answered in the array of the same name.
/// </remarks>
public sealed record SyncUploadRequest(
    Guid BatchId,
    int ContractVersion,
    IReadOnlyList<SyncUploadRowDto>? Rows = null,
    IReadOnlyList<string>? ClientFeatures = null,
    IReadOnlyList<SyncUploadPersonDto>? People = null,
    IReadOnlyList<SyncUploadDocumentDto>? Documents = null,
    IReadOnlyList<SyncUploadDocumentLinkDto>? DocumentLinks = null,
    IReadOnlyList<SyncUploadTripRecordingDto>? TripRecordings = null,
    IReadOnlyList<SyncUploadTripPointDto>? TripPoints = null,
    IReadOnlyList<SyncUploadRasterMapDto>? RasterMaps = null,
    IReadOnlyList<SyncUploadRasterPinDto>? RasterPins = null);

/// <summary>One of the phone's own users, as the phone describes them.</summary>
/// <param name="DeviceUserId">The phone's identifier for this user, adopted verbatim and never translated.</param>
/// <param name="Username">The user name the phone shows.</param>
/// <param name="CaverId">
/// The caver this user is, chosen from the people directory; absent or null says nothing about
/// the link, so a phone that has not been told leaves an existing link as it stands. A caver the
/// directory does not list for this account is refused, and a link somebody made in the browser
/// is kept whatever a phone sends — the directory's <c>links</c> say what stands.
/// </param>
public sealed record SyncUploadPersonDto(
    Guid DeviceUserId,
    string Username,
    string? FirstName = null,
    string? LastName = null,
    Guid? CaverId = null);

/// <summary>One photo or document the phone created, changed or removed.</summary>
/// <param name="Id">
/// The phone's identifier for the document, adopted verbatim when the document is created here,
/// as a feature row's is.
/// </param>
/// <param name="BaseRevision">
/// The document's revision as the phone last read it, from the catalogue or from the answer to
/// the upload that wrote it. Absent for a document the phone created; a stale one is a conflict.
/// </param>
/// <param name="Deleted">Removes the document; it needs the right to remove it, not only to edit it.</param>
/// <param name="Title">The document's name. Absent on a create, the file name stands in.</param>
/// <param name="Description">A photo's caption. Ignored for a file that is not a picture.</param>
/// <param name="FileName">The file's name on the phone; the staged transfer's own name otherwise.</param>
/// <param name="Sha256">
/// The lowercase hex SHA-256 of the content the document has on the phone. Required on every row
/// that is not a removal, except an edit that names its <paramref name="BaseVersionId"/> and
/// brings no bytes. Without a base version, the same hash as the current revision here says the
/// bytes did not change and a different one says they did.
/// </param>
/// <param name="SizeBytes">The content's size, checked against the staged bytes when stated.</param>
/// <param name="StagedUploadId">
/// The staged transfer holding the bytes, when the row brings new ones — always on a create.
/// </param>
/// <param name="CapturedAt">
/// When the content was made, as the phone recorded it. Kept only where the file's own metadata
/// states nothing, because the file is the better witness.
/// </param>
/// <param name="CreatedByDeviceUserId">
/// The phone user who added the file, when the phone records one. Kept on the document a create
/// makes and returned by the catalogue; a photo that names no photographer is credited to the
/// caver that phone user is linked to.
/// </param>
/// <param name="ClientUpdatedAt">When the phone last changed the row. Informational.</param>
/// <param name="BaseVersionId">
/// The revision of the bytes the phone holds, as the catalogue's <c>versionId</c> named it. A
/// phone handed a copy without location data, or only a rendition, holds bytes whose hash is not
/// the stored file's, so the hash alone cannot tell its caption edit from new content. With a base
/// version stated, a row naming no staged bytes changes only what is said about the document, a
/// row naming staged bytes of another hash than the stored file brings a new revision, and a base
/// version that is no longer the current one is a conflict.
/// </param>
public sealed record SyncUploadDocumentDto(
    Guid Id,
    DateTimeOffset? BaseRevision = null,
    bool Deleted = false,
    string? Title = null,
    string? Description = null,
    string? FileName = null,
    string? Sha256 = null,
    long? SizeBytes = null,
    Guid? StagedUploadId = null,
    DateTimeOffset? CapturedAt = null,
    Guid? CreatedByDeviceUserId = null,
    DateTimeOffset? ClientUpdatedAt = null,
    Guid? BaseVersionId = null);

/// <summary>One document hung on an object, or taken down from it.</summary>
/// <remarks>
/// A link is named by its document and its target together: the phone keeps no identifier of its
/// own for one, and a document hangs on an object once. Its answer is in the per-row shape, in the
/// order the links were sent, and carries the link's identifier here — the identifier the
/// documents catalogue names the link by, and its removals later.
/// </remarks>
/// <param name="DocumentId">The document, sent in this upload or earlier, or read from the catalogue.</param>
/// <param name="Target">What the document hangs on.</param>
/// <param name="Role">
/// What the file is on that object. Absent on a new link, it follows from the file and the
/// object — a picture of an entrance, of the inside of a cave, a document — and absent on an
/// existing one, the role it has stands.
/// </param>
/// <param name="Caption">The link's own caption, what the file is called on that object. Absent leaves it as it is.</param>
/// <param name="SortOrder">Where the file sits among the object's files. Absent leaves it as it is.</param>
/// <param name="Deleted">Takes the document down from the object; the document itself stays.</param>
public sealed record SyncUploadDocumentLinkDto(
    Guid DocumentId,
    SyncLinkTargetDto Target,
    AttachmentRole? Role = null,
    string? Caption = null,
    int? SortOrder = null,
    bool Deleted = false);

/// <summary>What became of one uploaded row.</summary>
/// <param name="Revision">
/// The row's server revision after the write, and the value the device sends back as its base
/// revision next time. Null when nothing was written.
/// </param>
/// <param name="Code">The stable reason a row was refused or lost a conflict; null otherwise.</param>
/// <param name="ReviewId">
/// For a held row, the identity its decision will be reported under; absent otherwise, so an
/// answer to an account nobody reviews is exactly what it always was.
/// </param>
public sealed record SyncUploadRowResultDto(
    Guid Id,
    SyncRowStatus Status,
    DateTimeOffset? Revision,
    string? Code,
    string? Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ReviewId = null);

/// <summary>
/// The answer to one upload, and the record of it: sending the same batch identifier again
/// returns this same document.
/// </summary>
/// <param name="ImportBatchId">
/// The batch this upload was recorded as. Every upload becomes one, which is what lets a caver
/// see what a phone put into the registry and take all of it back in a single act.
/// </param>
/// <param name="Replayed">
/// True when this answer was recorded by an earlier request carrying the same batch identifier
/// and nothing was written this time.
/// </param>
/// <param name="Conflicts">
/// This server's own version of each row that lost a conflict, in the shape a download would have
/// delivered it, so a device can show a caver what it is being asked to merge against instead of
/// making them go and fetch it.
/// </param>
/// <remarks>
/// The echo is a list beside the decisions rather than a field inside them, and that is the point
/// rather than a layout preference. The decisions are recorded with the batch so a resend can be
/// answered; a row carried inside them would be a position written into that record, handed back
/// on every later retry under whatever rights the account holds by then — and an account's right
/// to see a position can be taken away. Kept apart, the record cannot hold one, and the echo is
/// worked out afresh every time the answer is given.
///
/// A row that lost a conflict and is missing from this list is a row whose position this caller
/// may not have. That is the same answer the download gives — absence, never a blurred stand-in —
/// and the device is expected to read it the same way: the row changed, and re-reading it is how
/// to find out what to.
/// </remarks>
/// <param name="People">
/// What became of each phone user the upload described, in the per-row answer shape; present only
/// when the upload carried the section, so an upload of feature rows alone is answered exactly as
/// it always was.
/// </param>
/// <param name="Documents">
/// What became of each photo or document the upload sent, in the per-row answer shape, with the
/// document's revision; present only when the upload carried the section.
/// </param>
/// <param name="DocumentLinks">
/// What became of each link the upload sent, in the order sent, each answered under the link's
/// identifier — or, for a link refused before it existed, under its document's; present only when
/// the upload carried the section.
/// </param>
/// <param name="TripRecordings">
/// What became of each trip recording the upload carried; present only when it carried the section.
/// </param>
/// <param name="TripPoints">
/// What became of each scan the upload carried; present only when it carried the section.
/// </param>
/// <param name="RasterMaps">
/// What became of each raster map the upload carried; present only when it carried the section.
/// </param>
/// <param name="RasterPins">
/// What became of each pin the upload carried; present only when it carried the section.
/// </param>
/// <param name="Duplicates">
/// What was already here, near a row this batch created. A report and never a verdict: every row
/// listed was written, and its entry in <c>Rows</c> says so. It is here because a device is
/// offline when it decides to add a cave and cannot ask first, so the check that a file import
/// runs before committing has to run afterwards instead — and the caver, not the server, decides
/// whether the two rows are the same cave.
/// </param>
public sealed record SyncUploadResultDto(
    Guid BatchId,
    Guid ImportBatchId,
    bool Replayed,
    int Written,
    int Refused,
    IReadOnlyList<SyncUploadRowResultDto> Rows,
    IReadOnlyList<SyncFeatureDto> Conflicts,
    IReadOnlyList<SyncDuplicateDto> Duplicates,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SyncUploadRowResultDto>? People = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SyncUploadRowResultDto>? Documents = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SyncUploadRowResultDto>? DocumentLinks = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SyncUploadRowResultDto>? TripRecordings = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SyncUploadRowResultDto>? TripPoints = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SyncUploadRowResultDto>? RasterMaps = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    IReadOnlyList<SyncUploadRowResultDto>? RasterPins = null);

/// <summary>One created row and what it landed next to.</summary>
public sealed record SyncDuplicateDto(Guid Id, IReadOnlyList<SyncDuplicateCandidateDto> Nearby);

/// <summary>
/// An existing row near an uploaded one, with enough to let a caver recognise it.
/// </summary>
/// <param name="CaveFeatureId">
/// For an entrance, the cave it belongs to — which is what lets a caver decide the row is a
/// second entrance of a cave already in the registry rather than a cave of its own.
/// </param>
/// <remarks>
/// The pool this is drawn from is what the caller may read <em>and</em> place exactly, decided by
/// the one place that decides it. "Something is within fifty metres of this point" is itself a
/// position, so a protected row this account may not place is not compared against and is not
/// reported — the honest cost of which is that a duplicate of it will not be noticed.
/// </remarks>
public sealed record SyncDuplicateCandidateDto(
    Guid Id,
    string? Name,
    FeatureKind Kind,
    double DistanceMeters,
    Guid? CaveFeatureId);

public sealed class SyncUploadRequestValidator : AbstractValidator<SyncUploadRequest>
{
    private const int MaxClientFeatures = 16;
    private const int MaxClientFeatureLength = 64;

    /// <summary>The width of the column a stored file's name is kept in.</summary>
    public const int StoredFileNameMaxLength = 255;

    /// <summary>The longest description or log a recording carries.</summary>
    private const int TripTextMax = 20_000;

    /// <summary>The longest note on one scan.</summary>
    private const int TripNoteMax = 4_000;

    /// <summary>The largest pixel coordinate a pin may carry: far beyond any picture, short of overflow.</summary>
    private const int PinPixelMax = 1_000_000;

    public SyncUploadRequestValidator()
    {
        RuleFor(x => x.BatchId).NotEmpty();

        // Counted over every section, the same way the ceiling is: an upload is empty only when
        // no array carries anything. The ceiling itself is checked by the handler against the
        // value the capabilities endpoint announces, rather than restated as a number here.
        RuleFor(x => x)
            .Must(request => SyncUploadSections.ItemCount(request) > 0)
            .OverridePropertyName(nameof(SyncUploadRequest.Rows))
            .WithMessage("An upload carries at least one item.");

        // Named behaviours, not free text: bounded so the list kept on the batch stays a list of
        // names whatever a client sends.
        RuleFor(x => x.ClientFeatures)
            .Must(features => features is null || features.Count <= MaxClientFeatures)
            .WithMessage($"An upload names at most {MaxClientFeatures} client features.");
        RuleForEach(x => x.ClientFeatures).NotEmpty().MaximumLength(MaxClientFeatureLength);

        // A batch is applied in one transaction, so a row named twice would be arbitrated against
        // a revision the earlier copy had already moved. Refused as a whole rather than
        // half-applied, because which of the two copies won would otherwise depend on order alone.
        RuleFor(x => x.Rows)
            .Must(rows => rows is null || rows.Select(r => r.Id).Distinct().Count() == rows.Count)
            .WithMessage("A row may appear only once in a batch.");

        RuleFor(x => x.People)
            .Must(people => people is null || people.Select(p => p.DeviceUserId).Distinct().Count() == people.Count)
            .WithMessage("A phone user may appear only once in a batch.");

        RuleFor(x => x.DocumentLinks)
            .Must(links => links is null
                || links.Select(l => (l.DocumentId, l.Target?.Kind, l.Target?.Id)).Distinct().Count() == links.Count)
            .WithMessage("A document may be linked to one object only once in a batch.");

        // The width of the column a link's caption is kept in, and the two halves of its key.
        RuleForEach(x => x.DocumentLinks).ChildRules(link =>
        {
            link.RuleFor(l => l.DocumentId).NotEmpty();
            link.RuleFor(l => l.Target).NotNull();
            link.RuleFor(l => l.Target.Id).NotEmpty().When(l => l.Target is not null);
            link.RuleFor(l => l.Target.Kind).IsInEnum().When(l => l.Target is not null);
            link.RuleFor(l => l.Role).IsInEnum();
            link.RuleFor(l => l.Caption).MaximumLength(500);
        });

        RuleFor(x => x.Documents)
            .Must(documents => documents is null || documents.Select(d => d.Id).Distinct().Count() == documents.Count)
            .WithMessage("A document may appear only once in a batch.");

        // The widths of the columns each member is kept in: the document's title, a photo's
        // caption and a file's name (the stored file's name, which is narrower than a transfer's).
        RuleForEach(x => x.Documents).ChildRules(document =>
        {
            document.RuleFor(d => d.Id).NotEmpty();
            document.RuleFor(d => d.Title).MaximumLength(300);
            document.RuleFor(d => d.Description).MaximumLength(1000);
            document.RuleFor(d => d.FileName).MaximumLength(StoredFileNameMaxLength);
            document.RuleFor(d => d.Sha256)
                .NotEmpty()
                .When(d => !d.Deleted && (d.BaseVersionId is null || d.StagedUploadId is not null))
                .WithMessage("A document that is not being removed states the hash of its content.");
            document.RuleFor(d => d.BaseVersionId).NotEqual(Guid.Empty);
            document.RuleFor(d => d.Sha256)
                .Matches("^[0-9a-fA-F]{64}$")
                .When(d => d.Sha256 is not null)
                .WithMessage("The hash is a SHA-256 in hexadecimal.");
            document.RuleFor(d => d.SizeBytes).GreaterThan(0).When(d => d.SizeBytes is not null);
            document.RuleFor(d => d.StagedUploadId).NotEqual(Guid.Empty);
        });

        // The widths of the columns the description is kept in.
        RuleForEach(x => x.People).ChildRules(person =>
        {
            person.RuleFor(p => p.DeviceUserId).NotEmpty();
            person.RuleFor(p => p.Username).NotEmpty().MaximumLength(200);
            person.RuleFor(p => p.FirstName).MaximumLength(200);
            person.RuleFor(p => p.LastName).MaximumLength(200);
            person.RuleFor(p => p.CaverId).NotEqual(Guid.Empty);
        });

        RuleFor(x => x.TripRecordings)
            .Must(items => items is null || items.Select(r => r.Id).Distinct().Count() == items.Count)
            .WithMessage("A recording may appear only once in a batch.");
        RuleFor(x => x.TripPoints)
            .Must(items => items is null || items.Select(p => p.Id).Distinct().Count() == items.Count)
            .WithMessage("A scan may appear only once in a batch.");

        // A removal names the recording and nothing else; anything written needs its cave and
        // its start. The widths are the columns' own.
        RuleForEach(x => x.TripRecordings).ChildRules(recording =>
        {
            recording.RuleFor(r => r.Id).NotEmpty();
            recording.RuleFor(r => r.CaveId).NotNull().NotEqual(Guid.Empty).When(r => !r.Deleted);
            recording.RuleFor(r => r.StartedAt).NotNull().When(r => !r.Deleted);
            recording.RuleFor(r => r.Title).MaximumLength(255);
            recording.RuleFor(r => r.Description).MaximumLength(TripTextMax);
            recording.RuleFor(r => r.Log).MaximumLength(TripTextMax);
            recording.RuleFor(r => r.CreatedByDeviceUserId).NotEqual(Guid.Empty);
        });

        RuleFor(x => x.RasterMaps)
            .Must(items => items is null || items.Select(m => m.Id).Distinct().Count() == items.Count)
            .WithMessage("A raster map may appear only once in a batch.");
        RuleFor(x => x.RasterPins)
            .Must(items => items is null || items.Select(p => p.Id).Distinct().Count() == items.Count)
            .WithMessage("A pin may appear only once in a batch.");

        // A removal names the map and nothing else; anything written needs its cave, title and
        // kind, and a new map its picture. The widths are the columns' own.
        RuleForEach(x => x.RasterMaps).ChildRules(map =>
        {
            map.RuleFor(m => m.Id).NotEmpty();
            map.RuleFor(m => m.CaveId).NotNull().NotEqual(Guid.Empty).When(m => !m.Deleted);
            map.RuleFor(m => m.CaveAreaId).NotEqual(Guid.Empty);
            map.RuleFor(m => m.Title).NotEmpty().When(m => !m.Deleted && m.BaseRevision is null);
            map.RuleFor(m => m.Title).MaximumLength(255);
            map.RuleFor(m => m.MapType).NotNull().When(m => !m.Deleted);
            map.RuleFor(m => m.MapType).IsInEnum().When(m => m.MapType is not null);
            map.RuleFor(m => m.FileName).MaximumLength(StoredFileNameMaxLength);
            map.RuleFor(m => m.Sha256)
                .NotEmpty()
                .When(m => !m.Deleted && m.BaseRevision is null)
                .WithMessage("A new raster map states the hash of its picture.");
            map.RuleFor(m => m.Sha256)
                .Matches("^[0-9a-fA-F]{64}$")
                .When(m => m.Sha256 is not null)
                .WithMessage("The hash is a SHA-256 in hexadecimal.");
            map.RuleFor(m => m.SizeBytes).GreaterThan(0).When(m => m.SizeBytes is not null);
            map.RuleFor(m => m.StagedUploadId).NotEqual(Guid.Empty);
        });

        // A pixel is a position on the picture: not negative, and small enough that the sum of
        // two stays an integer.
        RuleForEach(x => x.RasterPins).ChildRules(pin =>
        {
            pin.RuleFor(p => p.Id).NotEmpty();
            pin.RuleFor(p => p.RasterMapId).NotEmpty();
            pin.RuleFor(p => p.PlaceId).NotNull().NotEqual(Guid.Empty).When(p => !p.Deleted);
            pin.RuleFor(p => p.X).NotNull().InclusiveBetween(0, PinPixelMax).When(p => !p.Deleted);
            pin.RuleFor(p => p.Y).NotNull().InclusiveBetween(0, PinPixelMax).When(p => !p.Deleted);
        });

        RuleForEach(x => x.TripPoints).ChildRules(point =>
        {
            point.RuleFor(p => p.Id).NotEmpty();
            point.RuleFor(p => p.RecordingId).NotEmpty();
            point.RuleFor(p => p.ScannedAt).NotNull().When(p => !p.Deleted);
            point.RuleFor(p => p.PlaceId).NotEqual(Guid.Empty);
            point.RuleFor(p => p.Notes).MaximumLength(TripNoteMax);
            point.RuleFor(p => p.CreatedByDeviceUserId).NotEqual(Guid.Empty);
        });

        RuleForEach(x => x.Rows).ChildRules(row =>
        {
            row.RuleFor(r => r.Id).NotEmpty();
            row.RuleFor(r => r.Kind).IsInEnum();
            // The column's own width, not a rounder number beside it: a name in between would
            // pass here and be truncated by the database, which surfaces as a failed batch the
            // device can only retry into the same failure for ever.
            row.RuleFor(r => r.Name).MaximumLength(255);
            row.RuleFor(r => r.FeatureTypeCode).MaximumLength(50);
            row.RuleFor(r => r.CaveTypeCode).MaximumLength(50);
            row.RuleFor(r => r.EntranceTypeCode).MaximumLength(50);
            row.RuleFor(r => r.PositionQuality).IsInEnum().When(r => r.PositionQuality is not null);

            // The property document is stored as given and never interpreted, so the only thing
            // worth asserting about it is that it is a document at all — a bare number would be
            // accepted by the column and would then have nowhere to put a key.
            row.RuleFor(r => r.Properties)
                .Must(p => p is null || p.Value.ValueKind is JsonValueKind.Object)
                .WithMessage("Properties must be a JSON object.");
        });
    }
}
