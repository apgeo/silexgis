// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Common.Targets;

/// <summary>What a caller may see of a member's target, from its own domain's rules —
/// null on the member when the target is unreadable, and then nothing here (name, route,
/// thumbnail) leaks around that answer.</summary>
/// <param name="Path">Where the target sits in its own world's containment, outermost
/// first — the names only, since nothing addresses a step. Two passages called "Galeria
/// Mare" are the same chip without it. Filled by the worlds that have a containment to
/// speak of and left empty by the rest, and it obeys the same reading rules as everything
/// else here: a step the caller may not read ends the path rather than being skipped over,
/// so a path never names something its reader is not allowed to know about.</param>
/// <param name="MediaType">The format of the file the target currently serves, for the
/// targets that serve one — otherwise null. Present so a reader can tell which of the
/// documents on a link opens in which viewer without asking about each of them in turn;
/// it says nothing a caller who may already read the document could not find out by
/// opening it, and it is filled in only on the same branch that decided they may.</param>
public sealed record ResLinkTargetDisplayDto(
    string Title,
    string? Subtitle,
    string? Route,
    string? ThumbnailUrl,
    IReadOnlyList<string>? Path = null,
    string? MediaType = null);

/// <summary>One row of the target picker feed — deliberately uniform across types so the
/// picker stays type-agnostic.</summary>
public sealed record ResLinkTargetHitDto(Guid Id, string Title, string? Subtitle);
