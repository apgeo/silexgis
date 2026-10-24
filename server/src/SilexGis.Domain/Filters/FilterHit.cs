// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Filters;

/// <summary>
/// One row of an answer, in the shape every world returns it.
/// </summary>
/// <remarks>
/// <para>
/// A hit carries no coordinates, and that is the point rather than an oversight. Somewhere on this
/// installation there is exactly one piece of code that decides how much of a protected position a
/// caller is shown, and it belongs to the endpoints that draw maps. A selector that carried its own
/// coordinates — even obfuscated ones — would be a second such place, and the second place is
/// always the one that is wrong after a year. Anything that needs to put a result on a map asks the
/// map for it.
/// </para>
/// <para>
/// What it does carry is <see cref="Placeable"/>: whether this caller may be shown this row's exact
/// position at all. That is not a position, it is a permission, and the selector needs it to know
/// whether "show me where this is" is an offer it can make.
/// </para>
/// </remarks>
public sealed record FilterHit
{
    /// <summary>Which world answered — the same key the scope asked under.</summary>
    public required string World { get; init; }

    public required Guid Id { get; init; }

    /// <summary>What to show. Never empty: a world with nothing to call a row names it anyway.</summary>
    public required string Title { get; init; }

    /// <summary>
    /// The quieter second line — a type, a cabinet, a date. Free text already resolved by the
    /// world, because only the world knows what is worth saying about its own rows.
    /// </summary>
    public string? Subtitle { get; init; }

    /// <summary>
    /// Which symbol to draw, as a key the client resolves. A key rather than an image so the same
    /// answer can be rendered by a list, a map legend and a printed sheet without three encodings.
    /// </summary>
    public string? Symbol { get; init; }

    /// <summary>Whether this caller may be shown where this row actually is.</summary>
    public bool Placeable { get; init; }

    /// <summary>
    /// Which of the row's other names matched the typed text, when the row was found by one rather
    /// than by its title — so a list can say why a row whose title does not contain what was typed
    /// is there. Null when the title matched, or when nothing was typed.
    /// </summary>
    public FilterHitMatch? Matched { get; init; }
}

/// <summary>
/// The field that found a row, and that field's value as the row holds it.
/// </summary>
/// <param name="Field">The world's own field key — for a feature, its other names or its code.</param>
/// <param name="Text">The value of that field, as shown on the row's own page.</param>
public sealed record FilterHitMatch(string Field, string Text);

/// <summary>
/// What one world is being asked, for one page.
/// </summary>
/// <param name="Caller">Who is asking. Every world composes its own visibility from this.</param>
/// <param name="Where">The condition tree, or null for everything this world holds.</param>
/// <param name="Anchor">
/// What the proximity sort measures from, as the request named it. Present only when the sort is
/// proximity and this world declares that sort; every other world is never handed one. The world
/// resolves it under <paramref name="Caller"/>'s own access, through the same gate a spatial
/// condition's anchor goes through: an object the caller cannot read or cannot place exactly is
/// refused in the same words as one that does not exist, before any row is ordered by it.
/// </param>
/// <param name="Ids">
/// When present, only these rows — for a control describing a choice somebody already made.
/// Applied beside the visibility walk rather than as a condition somebody could author, so naming
/// an id is never a way to find out whether it exists: an id the caller may not see comes back
/// missing, exactly as an id that was never there does.
/// </param>
/// <param name="WantTotal">
/// Whether the count is wanted. Asked for rather than always computed, because a count over a
/// world nobody is looking at is a query somebody pays for, and because at wide scope the counts
/// are deliberately not shown.
/// </param>
public sealed record WorldQuery(
    AccessContext Caller,
    FilterNode? Where,
    SortKey Sort,
    bool Descending,
    FilterAnchor? Anchor,
    int Skip,
    int Take,
    bool WantTotal,
    IReadOnlyCollection<Guid>? Ids = null);

/// <summary>
/// One world's answer.
/// </summary>
/// <param name="Total">
/// Null when it was not asked for. Null rather than zero, so "we did not count" and "there are
/// none" stay distinguishable — a screen that showed 0 for the first would be lying.
/// </param>
public sealed record WorldPage(IReadOnlyList<FilterHit> Hits, int? Total);
