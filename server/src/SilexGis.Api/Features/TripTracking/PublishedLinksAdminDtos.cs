// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using SilexGis.Api.Common;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Trips;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>What the installation-wide list of published links may be ordered by.</summary>
public enum PublishedLinkSort
{
    /// <summary>When the link was handed out. The default, newest first.</summary>
    CreatedAt = 0,

    /// <summary>When the link runs out by itself.</summary>
    ExpiresAt = 1,

    /// <summary>The first day of the trip the link is for.</summary>
    TripDate = 2,

    /// <summary>The trip's title, compared without regard to case.</summary>
    TripTitle = 3,

    /// <summary>What the link is doing, the links doing the most first.</summary>
    Status = 4,
}

/// <summary>
/// The questions the list of published links takes.
/// </summary>
/// <remarks>
/// Each member is bound by its own lower-camel-case name, which is what the contract publishes and
/// what every other route here is asked in. The two closed vocabularies arrive as text and are
/// parsed by name: bound straight to their enums they would accept a number or a comma-separated
/// list, and fail as a server fault on the camel-case spelling the answers themselves use.
/// </remarks>
/// <param name="Status">Keeps only links with this status, spelled as the answer spells it.</param>
/// <param name="Sort">What to order by; when absent, by when each link was handed out.</param>
/// <param name="Descending">
/// The direction. Absent means newest first for the default order and ascending for a named one.
/// </param>
/// <param name="Page">The page, from one.</param>
/// <param name="PageSize">How many links a page holds.</param>
public sealed record PublishedLinksQuery(
    [property: FromQuery(Name = "status")] string? Status,
    [property: FromQuery(Name = "sort")] string? Sort,
    [property: FromQuery(Name = "descending")] bool? Descending,
    [property: FromQuery(Name = "page")] int? Page,
    [property: FromQuery(Name = "pageSize")] int? PageSize);

public sealed class PublishedLinksQueryValidator : AbstractValidator<PublishedLinksQuery>
{
    public PublishedLinksQueryValidator()
    {
        // The two closed vocabularies are not judged here. A word that names no member is refused
        // by the route itself, under the code every closed vocabulary on a query string is refused
        // by, so a client can tell "not a word of this list" from "out of range" by the code alone.
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .When(x => x.Page is not null)
            .OverridePropertyName("page")
            .WithMessage("page must be 1 or more.");

        // Refused rather than clamped: a page silently shorter than the one asked for reads, on a
        // list somebody is about to act on, as "that is all there is".
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, Paging.MaxPageSize)
            .When(x => x.PageSize is not null)
            .OverridePropertyName("pageSize")
            .WithMessage($"pageSize must be between 1 and {Paging.MaxPageSize}.");
    }
}

/// <summary>The cave a published watch is anchored to.</summary>
/// <param name="Name">
/// The cave's name, or null when it has none or the cave itself is gone. Carried to full
/// administrators only, who may read every cave; no position travels with it.
/// </param>
public sealed record PublishedLinkCaveDto(Guid Id, string? Name);

/// <summary>
/// One published link as an administrator sees it: what it is for, who handed it out, and what it
/// does for its holder right now.
/// </summary>
/// <remarks>
/// <b>Never the token, and never the stored hash.</b> The token exists in one response only, the
/// one that mints it, and is not stored. The hash is what a token is looked up by, so a list that
/// printed it would hand a reader of this response the lookup key of every link on the
/// installation; only <paramref name="Handle"/> — a short prefix of it — is carried.
/// </remarks>
/// <param name="Id">The link's own id — what revoking or replacing it is asked by.</param>
/// <param name="Handle">
/// The first characters of the link's stored hash: the same handle the request log writes in place
/// of the token, so a line in the log and a row in this list can be matched by eye. It identifies
/// a link among the installation's links and opens nothing.
/// </param>
/// <param name="TripDate">The trip's first day, as the club wrote it down.</param>
/// <param name="Cave">
/// The cave the watch is anchored to, or null when the watch has none — in which case the link is
/// withheld or lapsed, never open.
/// </param>
/// <param name="WatchState">The watch the link follows.</param>
/// <param name="WatchClosedAt">When that watch was closed, or null while it has not been.</param>
/// <param name="CreatedByLabel">How the account that published it is shown, or null when it is gone.</param>
/// <param name="ExpiresAt">
/// When the link runs out by itself. An outer bound: closing the watch usually ends the live page
/// first, and a link that has run out still keeps its finished trip in the archive.
/// </param>
/// <param name="RevokedAt">When somebody took the link back, or null.</param>
/// <param name="Status">
/// What the link does for its holder at the instant the answer was computed, decided by the same
/// rules the published pages are served by.
/// </param>
/// <param name="ProtectedCaveWithinSurveyBounds">
/// A warning to look, never a finding: the link opens something, and the position of a protected
/// cave other than the trip's own, or of one of its entrances, lies inside the rectangle spanned
/// by the stations of the survey the link hands out. It is a check by position and cannot see
/// inside the file. False for a link that opens nothing, and for a survey with no stored stations.
/// It names no cave and carries no position.
/// </param>
public sealed record PublishedLinkDto(
    Guid Id,
    string Handle,
    Guid TripLogId,
    string TripTitle,
    DateOnly TripDate,
    DateOnly? TripDateEnd,
    PublishedLinkCaveDto? Cave,
    TripTrackingState WatchState,
    DateTimeOffset? WatchClosedAt,
    Guid CreatedBy,
    string? CreatedByLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    PublishedLinkStatus Status,
    bool ProtectedCaveWithinSurveyBounds);

/// <summary>How many of the installation's links have one status.</summary>
public sealed record PublishedLinkStatusCountDto(PublishedLinkStatus Status, int Count);

/// <summary>
/// A page of the installation's published links, with what a reader needs to understand it.
/// </summary>
/// <param name="TotalItems">How many links match the status asked for, across every page.</param>
/// <param name="AsOf">
/// The one instant every status in this answer was decided at. A status is a reading of a clock,
/// not a stored fact, so the answer says which reading it is.
/// </param>
/// <param name="Counts">
/// Every status with how many links have it, over the whole installation and whatever filter was
/// asked for — zero included, so a reader can tell "none" from "not counted".
/// </param>
/// <param name="PublishesRealNames">
/// Whether this installation's published pages name the party for real. An installation setting,
/// so it is said once here and is true of every link in the list; a caption on a person still
/// outranks it on the page itself.
/// </param>
/// <param name="ArchiveEnabled">
/// Whether this installation serves finished trips as history. With it off no link is ever in the
/// archive, and one whose live window has shut has lapsed.
/// </param>
public sealed record PublishedLinksDto(
    IReadOnlyList<PublishedLinkDto> Items,
    int Page,
    int PageSize,
    int TotalItems,
    DateTimeOffset AsOf,
    IReadOnlyList<PublishedLinkStatusCountDto> Counts,
    bool PublishesRealNames,
    bool ArchiveEnabled);

/// <summary>
/// What withdrawing every link of the installation has to be asked with.
/// </summary>
/// <param name="Confirm">
/// The word that says the caller means all of it: <see cref="Word"/>, exactly. Its only purpose is
/// that this act cannot be performed by a request that was meant for something else.
/// </param>
public sealed record RevokeEverythingRequest(string? Confirm)
{
    /// <summary>The word. Not a secret — it is in the contract — only something nobody sends by accident.</summary>
    public const string Word = "revoke-everything";

    internal const string Refusal = "confirm must be \"" + Word + "\": this takes back every published link and cannot be undone.";

    /// <summary>Compared exactly: a near miss is somebody who has not read what they are confirming.</summary>
    public static bool Confirms(string? confirm) => string.Equals(confirm, Word, StringComparison.Ordinal);
}

public sealed class RevokeEverythingRequestValidator : AbstractValidator<RevokeEverythingRequest>
{
    public RevokeEverythingRequestValidator()
    {
        RuleFor(x => x.Confirm)
            .Must(RevokeEverythingRequest.Confirms)
            .OverridePropertyName("confirm")
            .WithMessage(RevokeEverythingRequest.Refusal);
    }
}

/// <summary>What a withdrawal took back.</summary>
/// <param name="RevokedLinks">
/// How many links were standing and are now taken back. Links taken back earlier are not counted,
/// so asking twice answers zero the second time.
/// </param>
/// <param name="Trips">How many trips those links belonged to.</param>
public sealed record PublishedLinksWithdrawnDto(int RevokedLinks, int Trips);
