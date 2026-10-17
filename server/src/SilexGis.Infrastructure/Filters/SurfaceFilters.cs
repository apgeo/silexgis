// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.Filters;

namespace SilexGis.Infrastructure.Filters;

/// <summary>Why a surface declined the filter it was handed, as the problem it answers with.</summary>
public sealed record SurfaceFilterRefusal(int Status, string Code, string Detail);

/// <summary>
/// What a surface request's filter came to: a narrowing to apply, or a refusal to answer with.
/// </summary>
/// <param name="NearestFrom">
/// The place a link's own document asks its rows ranked from, when it asks for nearest first —
/// for a surface that offers that order. A saved document never carries one.
/// </param>
public sealed record SurfaceFilterResult<TEntity>(
    FilterNarrowing<TEntity>? Narrowing,
    SurfaceFilterRefusal? Refusal,
    FilterAnchor? NearestFrom = null)
    where TEntity : class;

/// <summary>
/// Turns what a list, an export or a map request names — an inline filter token or a saved
/// filter's id — into the narrowing for that surface's world, checked against the caller.
/// </summary>
/// <remarks>
/// <para>
/// One home for the steps every surface would otherwise repeat, and get wrong one at a time:
/// reading the token, finding the saved filter among those the caller may read, validating the
/// document against the <em>caller's</em> vocabulary rather than its author's, and picking out the
/// conditions about this surface's world. A saved filter is run under the runner's access; nothing
/// its author could see travels with it.
/// </para>
/// <para>
/// Every refusal is a refusal, never "everything". A request whose filter could not be read and was
/// answered unfiltered would export the whole registry while the person believed they had exported
/// a selection.
/// </para>
/// </remarks>
public sealed class SurfaceFilters(FilterWorldRegistry registry, SavedFilterReader savedFilters)
{
    public const string MalformedCode = "filter.malformed";
    public const string InvalidCode = "filter.invalid";
    public const string AmbiguousCode = "filter.ambiguous";
    public const string WrongWorldCode = "filter.wrong_world";
    public const string SavedNotFoundCode = "saved_filter.not_found";

    public async Task<SurfaceFilterResult<TEntity>> ForAsync<TEntity>(
        AccessContext caller,
        string world,
        string? filter,
        Guid? savedFilterId,
        CancellationToken ct)
        where TEntity : class
    {
        // An empty or blank `filter=` is no filter at all, unlike a token that cannot be read. A
        // link cut down to nothing lists everything the caller may see, which is what the same
        // address without the parameter lists, so reading it that way widens nothing; refusing it
        // would protect nothing either.
        var hasToken = !string.IsNullOrWhiteSpace(filter);
        if (!hasToken && savedFilterId is null)
        {
            return new(FilterNarrowing<TEntity>.Nothing, null);
        }

        if (hasToken && savedFilterId is not null)
        {
            // Two descriptions of the question, which may disagree; choosing one silently would
            // answer a question the person may not have meant.
            return Refused<TEntity>(400, AmbiguousCode, "Send a filter or a saved filter, not both.");
        }

        var vocabularies = await savedFilters.VocabulariesAsync(caller, ct);
        FilterDocument document;
        FilterAnchor? nearestFrom = null;
        if (hasToken)
        {
            // Null as well for a document that parses with a member missing from it, so a hole
            // in a link is the same refusal as a link cut short.
            var decoded = FilterToken.Decode(filter);
            if (decoded is null)
            {
                return Refused<TEntity>(400, MalformedCode, "This filter could not be read.");
            }

            // A point or an outline in a link's own document is read, like the rest of it: a link
            // is a request, handed on by somebody who chose to. So is the place it asks its rows
            // ranked from, which only a surface offering nearest first reads; it is checked here,
            // with the rest of the document, whether or not that surface asks for it. A saved
            // document never keeps either.
            var errors = FilterValidation.Validate(decoded, vocabularies);
            if (decoded is { Sort: SortKey.Proximity, Anchor: not null })
            {
                errors = [.. errors, .. SpatialConditions.ProximityProblems(decoded, vocabularies)];
                nearestFrom = decoded.Anchor;
            }

            if (errors.Count > 0)
            {
                return Refused<TEntity>(400, InvalidCode, string.Join(" ", errors));
            }

            document = decoded;
        }
        else
        {
            var reading = await savedFilters.ReadAsync(caller, savedFilterId!.Value, ct);
            if (reading is null)
            {
                return Refused<TEntity>(404, SavedNotFoundCode, "There is no such saved filter.");
            }

            if (reading.State == SavedFilterState.Unreadable)
            {
                // The same words whether a field is withheld from this reader or no longer exists:
                // telling them which would answer whether the field is there.
                return Refused<TEntity>(
                    400, SavedFilterRules.UnreadableCode, "This saved filter cannot be read with your access.");
            }

            document = reading.Document!;
        }

        if (SavedFilterRules.ScopeFor(document, world) is not { } scope)
        {
            return Refused<TEntity>(400, WrongWorldCode, "This filter is not about what this page lists.");
        }

        if (registry.Find(world) is not FilterWorld<TEntity> target)
        {
            throw new InvalidOperationException(
                $"No filter world '{world}' over {typeof(TEntity).Name}; a surface names its own world.");
        }

        try
        {
            return new(await target.NarrowingAsync(caller, scope.Where, ct), null, nearestFrom);
        }
        catch (FilterRefusedException refused)
        {
            // An anchor this caller cannot use. Refused, never read as "no condition": a surface
            // that dropped the condition would list everything while looking filtered.
            return Refused<TEntity>(400, refused.Code, refused.Detail);
        }
    }

    /// <summary>
    /// A surface's own rows nearest first: those the caller may place exactly by their distance
    /// from <paramref name="anchor"/>, every other row after them in the world's fallback order —
    /// the same order, through the same anchor gate, as the filter's own query gives.
    /// </summary>
    /// <remarks>
    /// Refused rather than answered in another order when there is nothing to measure from: an
    /// order that is not the one asked for looks exactly like the one asked for.
    /// </remarks>
    public async Task<(IQueryable<TEntity>? Rows, SurfaceFilterRefusal? Refusal)> NearestFirstAsync<TEntity>(
        AccessContext caller, string world, IQueryable<TEntity> rows, FilterAnchor? anchor, CancellationToken ct)
        where TEntity : class
    {
        if (anchor is null)
        {
            return (null, new SurfaceFilterRefusal(
                400, InvalidCode, "Nearest first needs a place to measure from, sent with the filter."));
        }

        if (registry.Find(world) is not FilterWorld<TEntity> target)
        {
            throw new InvalidOperationException(
                $"No filter world '{world}' over {typeof(TEntity).Name}; a surface names its own world.");
        }

        try
        {
            return (await target.NearestFirstAsync(caller, rows, anchor, ct), null);
        }
        catch (FilterRefusedException refused)
        {
            return (null, new SurfaceFilterRefusal(400, refused.Code, refused.Detail));
        }
    }

    private static SurfaceFilterResult<TEntity> Refused<TEntity>(int status, string code, string detail)
        where TEntity : class =>
        new(null, new SurfaceFilterRefusal(status, code, detail));
}
