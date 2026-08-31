// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using FluentValidation;
using SilexGis.Domain;

namespace SilexGis.Api.Features.Sync;

/// <summary>
/// What this installation can do, and which contract it speaks. A device asks for this
/// first: it is the only way a build in the field can find out whether the server it just
/// met understands it, and it is deliberately not folded into the general about endpoint,
/// whose version number moves for reasons that have nothing to do with this protocol.
/// </summary>
/// <param name="ContractVersion">
/// The protocol generation. A device pins the one it was built against and sends it back on
/// every write; a mismatch is answered rather than guessed at. Bumped only by a change that
/// breaks a pinned client — never by an addition, which is what <paramref name="Features"/>
/// is for.
/// </param>
/// <param name="PageSizeMax">The largest download page this installation will serve.</param>
/// <param name="UploadRowsMax">The most rows one upload batch may carry.</param>
/// <param name="Features">
/// The optional parts of the contract this installation actually serves. A device pinned to
/// this contract version can therefore meet a server that has not shipped every part of it
/// yet, and take the parts that are there.
/// </param>
public sealed record SyncCapabilitiesDto(
    int ContractVersion,
    int PageSizeMax,
    int UploadRowsMax,
    IReadOnlyList<string> Features);

/// <summary>
/// One sync set as its owner sees it — and, where an installation has turned that on, as a full
/// administrator sees somebody else's. Every field here is disclosed by that widening, the list of
/// carried caves included, which is why it is off until an installation asks for it.
/// </summary>
public sealed record SyncSetDto(
    Guid Id,
    string Name,
    Guid? CavingGroupId,
    Visibility UploadVisibility,
    IReadOnlyList<Guid> RootFeatureIds,
    JsonElement Settings,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Create or replace a sync set. Every field is stated; there is no partial write.</summary>
/// <param name="BaseRevision">
/// The set revision the caller last read, required when replacing an existing set and ignored
/// when creating one. It is what a replacement of the settings document is arbitrated on, and it
/// is the same rule an uploaded row is arbitrated on: the value the server last stamped, compared
/// for equality, never a clock.
///
/// Without it the two writers of a set — the settings page and the caver's own phone, which both
/// post the whole of it — cannot both be right. A phone that last read the set two weeks ago
/// would otherwise win unconditionally over a digit width the caver changed yesterday, and the
/// two devices would go on allocating place codes that do not fit together, with nothing anywhere
/// having reported a conflict.
/// </param>
public sealed record SyncSetWriteRequest(
    string Name,
    Guid? CavingGroupId,
    Visibility UploadVisibility,
    IReadOnlyList<Guid> RootFeatureIds,
    JsonElement Settings,
    long? BaseRevision = null);

public sealed class SyncSetWriteRequestValidator : AbstractValidator<SyncSetWriteRequest>
{
    /// <summary>
    /// How many roots one set may name. Not a storage limit — a root is one row — but a bound
    /// on what a single request can make the server resolve and check, since every named root
    /// costs a visibility check before the write is allowed to touch anything.
    /// </summary>
    public const int MaxRootFeatures = 500;

    public SyncSetWriteRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.UploadVisibility).IsInEnum();

        // Club visibility says "the club this belongs to may read it", so it needs a club to
        // name; without one it would be a band that admits nobody, silently. Here that would
        // not be one row but every row the device ever uploads through this set, each created
        // invisible to everybody except the account that made it and with nothing to say so.
        RuleFor(x => x.CavingGroupId).NotNull()
            .When(x => x.UploadVisibility == Visibility.CavingGroup)
            .WithMessage("Caving-group visibility needs a caving group.");

        RuleFor(x => x.RootFeatureIds).NotNull();
        RuleFor(x => x.RootFeatureIds)
            .Must(ids => ids is null || ids.Count <= MaxRootFeatures)
            .WithMessage($"A sync set names at most {MaxRootFeatures} roots.");
        RuleFor(x => x.RootFeatureIds)
            .Must(ids => ids is null || ids.Distinct().Count() == ids.Count)
            .WithMessage("A root may be named only once.");
        RuleForEach(x => x.RootFeatureIds).NotEmpty();

        // The settings document is stored verbatim and never interpreted, so the only thing
        // worth asserting about it is that it is a document at all: a bare number or string
        // would be accepted by jsonb and would then have nowhere to put a key.
        //
        // An absent property is refused rather than read as an empty document. A write here
        // replaces the whole set, so "absent" could only mean "clear it", and a caller that
        // simply did not echo the field back — a hand-made request, a client written against
        // half the contract — would silently wipe the code-generation settings the device
        // needs and hand it a new revision to re-read them by. Stating the field is cheap;
        // losing it is not, and the published contract already marks it required.
        RuleFor(x => x.Settings)
            .Must(s => s.ValueKind is JsonValueKind.Object)
            .WithMessage("Settings must be a JSON object.");
    }
}
