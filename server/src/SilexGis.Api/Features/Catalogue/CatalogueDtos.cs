// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using SilexGis.Domain;
using SilexGis.Infrastructure.Catalogue;

namespace SilexGis.Api.Features.Catalogue;

/// <summary>
/// Whether this installation can reach the Romanian cave catalogue, and the limits it works to.
/// </summary>
/// <param name="Configured">
/// False when no API key has been supplied. The screens are not offered in that case — an
/// installation without a key is an ordinary installation, not a broken one.
/// </param>
/// <param name="MaxPageSize">The largest page of results this installation will ask for.</param>
/// <param name="MaxSelection">The most caves one confirmation will import.</param>
/// <param name="PortalUrl">The catalogue's own site, for the link out.</param>
public sealed record SpeologieStatusDto(
    bool Configured,
    int MaxPageSize,
    int MaxSelection,
    string PortalUrl);

/// <summary>
/// One cave as the catalogue describes it, plus the one thing the catalogue cannot know: whether
/// this installation already holds it.
/// </summary>
/// <param name="Id">The catalogue's own identifier.</param>
/// <param name="Title">The cave's name, exactly as the catalogue writes it.</param>
/// <param name="Slug">The catalogue's own URL segment.</param>
/// <param name="Url">The cave's page on speologie.org, for the link out. Null when it has no slug.</param>
/// <param name="County">Two-letter Romanian county code.</param>
/// <param name="Locality">Nearest locality.</param>
/// <param name="Mountain">Mountain range, as the catalogue's slug.</param>
/// <param name="Length">Surveyed length in metres.</param>
/// <param name="Depth">Total vertical range in metres.</param>
/// <param name="NegativeDepth">Downward vertical range in metres.</param>
/// <param name="Altitude">Entrance altitude in metres.</param>
/// <param name="ProtectionClass">Statutory protection class, as a bare letter.</param>
/// <param name="Science">Scientific interest, normalised to one spacing.</param>
/// <param name="RockCode">The catalogue's rock code. No legend for it is published, so it is passed through as written.</param>
/// <param name="Sump">Whether the cave holds a sump.</param>
/// <param name="Vanished">Whether the catalogue records the cave as destroyed or lost.</param>
/// <param name="ProtectedAreaCode">Protected-area code.</param>
/// <param name="HydroNumber">Hydrologic number within its basin.</param>
/// <param name="HydroBasinId">The catalogue's basin identifier. Opaque — the catalogue does not publish the basin names through this interface.</param>
/// <param name="Description">
/// The description, already converted from the catalogue's markup to the plain text this
/// application stores, and already cut to the length it would be stored at. Only answered for a
/// single cave; a list never carries it, because one description in this catalogue can exceed a
/// megabyte on its own.
/// </param>
/// <param name="AlreadyImported">
/// Whether some cave here already carries this catalogue entry's identifier. Answered whether or
/// not the caller may see that cave, because it is the answer to "would importing this make a
/// duplicate", and that is true regardless of who is asking.
/// </param>
/// <param name="ExistingCaveId">
/// The cave it was imported as — but only when the caller may read it. A caller who may not is
/// told that the entry is already here and not which cave it is, which is the most that can be
/// said without disclosing a cave they were not given.
/// </param>
public sealed record SpeologieCaveDto(
    int Id,
    string Title,
    string? Slug,
    string? Url,
    string? County,
    string? Locality,
    string? Mountain,
    double? Length,
    double? Depth,
    double? NegativeDepth,
    double? Altitude,
    string? ProtectionClass,
    string? Science,
    string? RockCode,
    bool? Sump,
    bool Vanished,
    string? ProtectedAreaCode,
    string? HydroNumber,
    int? HydroBasinId,
    string? Description,
    bool AlreadyImported,
    Guid? ExistingCaveId);

/// <summary>
/// A page of catalogue results.
/// </summary>
/// <remarks>
/// There is no total. The catalogue publishes no count of what matches a search and offers no way
/// to compute one short of walking the whole result set, which is exactly the traffic this
/// integration exists to avoid — so this says whether there is another page rather than inventing
/// a number that would have to be wrong.
/// </remarks>
/// <param name="Items">The caves found, by ascending catalogue id.</param>
/// <param name="Page">The page answered, one-based.</param>
/// <param name="PageSize">How many were asked for.</param>
/// <param name="HasMore">Whether at least one spelling of the term had rows beyond this page.</param>
/// <param name="Spellings">
/// The spellings actually searched for. Romanian is written with two different diacritic
/// conventions and often with none, and the catalogue's search folds none of them together, so
/// one typed term is asked about under several spellings — and the screen says which, because a
/// search that quietly asked something other than what was typed is worse than one that did not.
/// </param>
public sealed record SpeologieSearchDto(
    IReadOnlyList<SpeologieCaveDto> Items,
    int Page,
    int PageSize,
    bool HasMore,
    IReadOnlyList<string> Spellings);

/// <summary>What one person decided about one catalogue cave.</summary>
/// <param name="Action">
/// <c>create</c>, <c>update</c> or <c>skip</c>. Omitted, a cave already imported is refreshed and
/// one that is not is created.
/// </param>
/// <param name="CaveTypeCode">Overrides the type worked out from the cave's name.</param>
/// <param name="Longitude">Where the cave is, if the person placed it. Both ordinates or neither.</param>
/// <param name="Latitude">See <paramref name="Longitude"/>.</param>
public sealed record SpeologieDecisionDto(
    SpeologieAction? Action = null,
    string? CaveTypeCode = null,
    double? Longitude = null,
    double? Latitude = null);

/// <summary>Confirming a selection of catalogue caves.</summary>
/// <param name="Selection">The catalogue ids to import, in the order they are to be dealt with.</param>
/// <param name="Decisions">Per-cave choices, keyed by catalogue id as a string. Absent means "as proposed".</param>
/// <param name="Visibility">Who may see what is created.</param>
/// <param name="CavingGroupId">The group the created caves belong to, if any.</param>
/// <param name="LocationProtected">Whether the created caves are protection roots.</param>
/// <param name="ParentId">A feature to file the created caves under, if any.</param>
public sealed record SpeologieImportRequest(
    IReadOnlyList<int> Selection,
    IReadOnlyDictionary<string, SpeologieDecisionDto>? Decisions,
    Visibility Visibility,
    Guid? CavingGroupId,
    bool LocationProtected,
    Guid? ParentId);

/// <summary>One cave that could not be imported, and why.</summary>
/// <param name="SpeologieId">The catalogue id it was asked for under.</param>
/// <param name="Title">Its name, when the catalogue gave one.</param>
/// <param name="Code">The stable machine code.</param>
/// <param name="Reason">A sentence for the person reading it.</param>
public sealed record SpeologieFailureDto(int SpeologieId, string? Title, string Code, string Reason);

/// <summary>What one confirmation did.</summary>
/// <param name="BatchId">The import batch, which is also what reverses it.</param>
/// <param name="CreatedCount">Caves created.</param>
/// <param name="UpdatedCount">Caves already here that were refreshed from the catalogue.</param>
/// <param name="SkippedCount">Caves passed over.</param>
/// <param name="Failures">Caves that could not be dealt with, listed rather than counted.</param>
public sealed record SpeologieImportResultDto(
    Guid BatchId,
    int CreatedCount,
    int UpdatedCount,
    int SkippedCount,
    IReadOnlyList<SpeologieFailureDto> Failures);

/// <summary>
/// A search has to name something. Asking the catalogue for everything, one page at a time, is
/// how a small volunteer-run service gets crawled by accident, and this application declines to
/// offer the button.
/// </summary>
public sealed class SpeologieSearchValidator : AbstractValidator<SpeologieSearchQueryDto>
{
    public SpeologieSearchValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Q) || !string.IsNullOrWhiteSpace(x.County))
            .WithMessage("Give a name to search for, a county, or both.");

        RuleFor(x => x.Q).MaximumLength(200);

        RuleFor(x => x.County)
            .Matches("^[A-Za-z]{2}$")
            .When(x => !string.IsNullOrWhiteSpace(x.County))
            .WithMessage("A county is the two-letter code the catalogue uses, such as BH or GJ.");
    }
}

/// <summary>The search's parameters, as one object so they can be validated as one.</summary>
/// <param name="Q">Free text, matched by the catalogue against a cave's name and its URL slug.</param>
/// <param name="County">Two-letter Romanian county code.</param>
/// <param name="Page">One-based page number.</param>
/// <param name="PageSize">How many rows are wanted.</param>
public sealed record SpeologieSearchQueryDto(string? Q, string? County, int? Page, int? PageSize);

/// <summary>
/// A confirmation names caves and nothing else — there is no "import everything that matched",
/// because reviewing is the whole point of the screen it comes from.
/// </summary>
public sealed class SpeologieImportValidator : AbstractValidator<SpeologieImportRequest>
{
    public SpeologieImportValidator()
    {
        RuleFor(x => x.Selection).NotEmpty();
        RuleForEach(x => x.Selection).GreaterThan(0);
        RuleFor(x => x.Visibility).IsInEnum();

        RuleForEach(x => x.Decisions!)
            .Must(pair => pair.Value is null || IsPlacedOrUnplaced(pair.Value))
            .When(x => x.Decisions is not null)
            .WithMessage("A position needs both a longitude and a latitude, or neither.");

        RuleForEach(x => x.Decisions!)
            .Must(pair => pair.Value?.Longitude is not { } lon || lon is >= -180 and <= 180)
            .When(x => x.Decisions is not null)
            .WithMessage("A longitude is between -180 and 180.");

        RuleForEach(x => x.Decisions!)
            .Must(pair => pair.Value?.Latitude is not { } lat || lat is >= -90 and <= 90)
            .When(x => x.Decisions is not null)
            .WithMessage("A latitude is between -90 and 90.");
    }

    private static bool IsPlacedOrUnplaced(SpeologieDecisionDto d) =>
        (d.Longitude is null) == (d.Latitude is null);
}
