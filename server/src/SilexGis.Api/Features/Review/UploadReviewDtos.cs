// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Import;

namespace SilexGis.Api.Features.Review;

/// <summary>One held batch as the review queue and the uploader list it.</summary>
/// <param name="PendingCount">Lines still waiting for a decision.</param>
/// <param name="LineCount">Every line the batch holds, decided or not, superseded ones included.</param>
/// <param name="HeldBytes">
/// The size of the files the batch's waiting lines keep on the server until they are decided.
/// </param>
/// <param name="Sections">How many waiting lines each part of the upload holds, by its name.</param>
/// <param name="UploaderName">The uploading account's display label, never its address.</param>
/// <param name="DecidedByName">The deciding account's display label, once a line was decided.</param>
public sealed record HeldBatchDto(
    Guid Id,
    ImportSource Source,
    Guid UploaderId,
    ImportReviewState ReviewState,
    DateTimeOffset? HeldAt,
    DateTimeOffset? DecidedAt,
    Guid? DecidedByUserId,
    string? ReviewNote,
    int PendingCount,
    int LineCount,
    long HeldBytes,
    IReadOnlyDictionary<string, int> Sections,
    string? UploaderName = null,
    string? DecidedByName = null);

/// <summary>One held line: what it proposes and what became of it.</summary>
/// <param name="ReviewId">The line's identity outside the server — the id a phone was answered with.</param>
/// <param name="Section">The part of the upload the line belongs to.</param>
/// <param name="RowId">
/// The row the proposal is about, in its section's identity; absent with the proposal when the
/// viewer may not read a row the line changes or names.
/// </param>
/// <param name="DependsOnReviewIds">The held lines this one cannot be written without.</param>
/// <param name="AcceptableFields">The fields this line may be accepted by on their own; empty when only whole.</param>
/// <param name="ChangedSince">
/// A waiting edit whose row was written on the server after the uploader read it: accepting it
/// needs <c>overCurrent</c>, which writes it over the row as it is now.
/// </param>
/// <param name="Proposal">
/// What the line would write, as the viewer may be shown it: without the position of a row the
/// viewer may not place, and absent when the line changes or names a row the viewer may not read.
/// </param>
/// <param name="Fields">
/// For a waiting edit of a photo or document, a recording, a scan or a raster map: each field it
/// may be accepted by, the row now beside the proposal. Absent for every other line — a feature
/// row's current state is read through the feature's own route.
/// </param>
/// <param name="HasPreview">
/// The line keeps a picture on the server — a photo or a raster map's image, still waiting or
/// turned down and not yet swept — of which <c>/review/items/{reviewId}/preview</c> draws a rendering.
/// </param>
public sealed record HeldLineDto(
    Guid ReviewId,
    string? Section,
    Guid? RowId,
    ImportDecisionAction Action,
    ImportReviewDecision Decision,
    IReadOnlyList<Guid> DependsOnReviewIds,
    IReadOnlyList<string>? AcceptedFields,
    IReadOnlyList<string> AcceptableFields,
    string? Note,
    DateTimeOffset? DecidedAt,
    Guid? DecidedByUserId,
    string? FailureCode,
    string? FailureDetail,
    bool ChangedSince,
    JsonElement? Proposal,
    IReadOnlyList<HeldFieldDto>? Fields = null,
    bool HasPreview = false);

/// <summary>One field of a waiting edit: the row now, and what the line would write.</summary>
/// <param name="Current">The value now; absent when the row holds none, or when it is withheld.</param>
/// <param name="Differs">Whether accepting the field changes the row; true whenever the value now is withheld.</param>
/// <param name="CurrentWithheld">
/// The value now names a row the viewer may not read — a recording's earlier cave, a scan's
/// earlier place — and is left out.
/// </param>
public sealed record HeldFieldDto(
    string Field, JsonElement? Current, JsonElement? Proposed, bool Differs, bool CurrentWithheld);

/// <summary>A held batch with its lines.</summary>
public sealed record HeldBatchDetailDto(HeldBatchDto Batch, IReadOnlyList<HeldLineDto> Lines);

/// <summary>What a decision did: the lines it decided and the batches it touched.</summary>
public sealed record ReviewDecisionResultDto(IReadOnlyList<HeldLineDto> Decided, IReadOnlyList<HeldBatchDto> Batches);

/// <summary>Accepting or rejecting a whole batch.</summary>
/// <param name="Note">For the uploader to read.</param>
/// <param name="OverCurrent">
/// Write an edit whose row changed on the server since it was sent over the row's current state.
/// Without it, such a row stops the whole decision with <c>review.changed_since</c>.
/// </param>
public sealed record ReviewBatchDecisionRequest(string? Note = null, bool? OverCurrent = null);

/// <summary>Accepting or rejecting one line.</summary>
/// <param name="Fields">
/// For an edit, the fields accepted when not all of them — any of the line's
/// <c>acceptableFields</c>: for a feature row <c>name</c>, <c>description</c>, <c>properties</c>,
/// <c>position</c>; for a photo or document <c>title</c>, <c>description</c>, <c>file</c>; for a
/// recording <c>title</c>, <c>description</c>, <c>log</c>, <c>times</c>, <c>cave</c>; for a scan
/// <c>notes</c>, <c>place</c>, <c>time</c>; for a raster map <c>title</c>, <c>mapType</c>,
/// <c>sortOrder</c>, <c>caveArea</c>, <c>file</c>. Ignored when rejecting.
/// </param>
public sealed record ReviewItemDecisionRequest(
    IReadOnlyList<string>? Fields = null, string? Note = null, bool? OverCurrent = null);

public sealed class ReviewBatchDecisionRequestValidator : AbstractValidator<ReviewBatchDecisionRequest>
{
    public ReviewBatchDecisionRequestValidator() =>
        RuleFor(x => x.Note).MaximumLength(2000);
}

public sealed class ReviewItemDecisionRequestValidator : AbstractValidator<ReviewItemDecisionRequest>
{
    public ReviewItemDecisionRequestValidator()
    {
        RuleFor(x => x.Note).MaximumLength(2000);
        RuleFor(x => x.Fields!)
            .Must(f => f.Count is > 0 and <= 8).WithMessage("Name between one and eight fields.")
            .Must(f => f.Distinct(StringComparer.Ordinal).Count() == f.Count).WithMessage("Name each field once.")
            .When(x => x.Fields is not null);
        RuleForEach(x => x.Fields).NotEmpty().MaximumLength(32);
    }
}
