// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
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
/// In dependency order: a cave before its entrances, an area before the places inside it. They
/// are applied in the order given, so a row whose container is created earlier in the same batch
/// finds it.
/// </param>
public sealed record SyncUploadRequest(
    Guid BatchId,
    int ContractVersion,
    IReadOnlyList<SyncUploadRowDto> Rows);

/// <summary>What became of one uploaded row.</summary>
/// <param name="Revision">
/// The row's server revision after the write, and the value the device sends back as its base
/// revision next time. Null when nothing was written.
/// </param>
/// <param name="Code">The stable reason a row was refused or lost a conflict; null otherwise.</param>
public sealed record SyncUploadRowResultDto(
    Guid Id,
    SyncRowStatus Status,
    DateTimeOffset? Revision,
    string? Code,
    string? Detail);

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
    IReadOnlyList<SyncDuplicateDto> Duplicates);

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
    public SyncUploadRequestValidator()
    {
        RuleFor(x => x.BatchId).NotEmpty();
        RuleFor(x => x.Rows).NotNull();

        // The ceiling is announced rather than discovered, so the validator asks the same source
        // the capabilities endpoint answers from instead of restating a number beside it.
        RuleFor(x => x.Rows)
            .Must(rows => rows is null || rows.Count > 0)
            .WithMessage("An upload carries at least one row.");

        // A batch is applied in one transaction, so a row named twice would be arbitrated against
        // a revision the earlier copy had already moved. Refused as a whole rather than
        // half-applied, because which of the two copies won would otherwise depend on order alone.
        RuleFor(x => x.Rows)
            .Must(rows => rows is null || rows.Select(r => r.Id).Distinct().Count() == rows.Count)
            .WithMessage("A row may appear only once in a batch.");

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
