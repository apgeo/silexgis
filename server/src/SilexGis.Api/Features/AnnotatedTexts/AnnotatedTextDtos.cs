// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain;
using SilexGis.Domain.Documents;

namespace SilexGis.Api.Features.AnnotatedTexts;

/// <summary>
/// A link-annotated text document as a reader receives it.
/// </summary>
/// <param name="DocumentId">The document's stable identity — what links anchor to, and what
/// survives every revision of the words.</param>
/// <param name="FileId">The revision's file. Anchors authored against this body pin it, so a
/// client composing one sends this back and never guesses.</param>
/// <param name="CanonicalLength">Characters in the stream anchors are measured against.
/// Stated so a client can refuse an out-of-range anchor before the round trip, and so a
/// mismatch between what the client rendered and what the server holds is detectable rather
/// than showing up as highlights a few characters out.</param>
/// <param name="MayWrite">Whether this caller may replace the body. Advisory — the server
/// re-asks — but without it the interface has to offer editing to everyone and let most of
/// them find out by being refused.</param>
public sealed record AnnotatedTextDto(
    Guid DocumentId,
    Guid FileId,
    string Title,
    Visibility Visibility,
    Guid? CavingGroupId,
    int VersionNumber,
    int CanonicalLength,
    bool MayWrite,
    IReadOnlyList<AnnotatedBlock> Blocks);

/// <summary>What became of the links over a body that was just replaced.</summary>
/// <param name="Unmoved">Passages whose recorded offsets still named them.</param>
/// <param name="Moved">Passages found again elsewhere and re-measured against the new text.</param>
/// <param name="Lost">Passages no longer present. Their links survive and now read as
/// degraded — deliberately, because the alternative is re-aiming them at words their author
/// never chose.</param>
public sealed record ReanchorReportDto(int Unmoved, int Moved, int Lost);

/// <summary>A replaced body, with what happened to the links over it.</summary>
public sealed record AnnotatedTextWriteDto(AnnotatedTextDto Text, ReanchorReportDto Reanchoring);

public sealed record AnnotatedTextCreateRequest(
    string Title,
    IReadOnlyList<AnnotatedBlock> Blocks,
    Visibility Visibility = Visibility.Private,
    Guid? CavingGroupId = null);

public sealed record AnnotatedTextReplaceRequest(IReadOnlyList<AnnotatedBlock> Blocks);

/// <summary>
/// Shape checks the request can be refused on without touching the database. The body's own
/// rules are not restated here — they live in the format itself, are what the stored bytes are
/// validated against on every path that writes them, and a second copy in a validator would be
/// the copy that drifts.
/// </summary>
public sealed class AnnotatedTextCreateRequestValidator : AbstractValidator<AnnotatedTextCreateRequest>
{
    public AnnotatedTextCreateRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Visibility).IsInEnum();
        RuleFor(x => x.Blocks).NotEmpty();

        // Club visibility says "the club this belongs to may read it", so it needs a club to
        // name; without one it would be a band that admits nobody, silently.
        RuleFor(x => x.CavingGroupId).NotNull()
            .When(x => x.Visibility == Visibility.CavingGroup)
            .WithMessage("Caving-group visibility needs a caving group.");
    }
}

public sealed class AnnotatedTextReplaceRequestValidator : AbstractValidator<AnnotatedTextReplaceRequest>
{
    public AnnotatedTextReplaceRequestValidator() => this.RuleFor(x => x.Blocks).NotEmpty();
}
