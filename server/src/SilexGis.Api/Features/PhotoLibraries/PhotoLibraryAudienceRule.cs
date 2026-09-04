// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;
using SilexGis.Domain.PhotoLibraries;

namespace SilexGis.Api.Features.PhotoLibraries;

/// <summary>
/// Who may see the neighbouring photo libraries. The whole of the authorisation model for this
/// feature, deliberately: two values, one predicate, asked in one place.
/// </summary>
/// <remarks>
/// <para>
/// It answers "may this account use this feature at all". It does not answer, and must not grow to
/// answer, "may this account see this photograph" — a question nothing here asks, because the
/// photographs on the other side are governed by that side's rules rather than by this
/// application's. A per-photograph rule would be asked between the library's answer and the feature
/// list, on the photograph rather than on the account, and would replace this predicate's role at
/// the picture route rather than sit beside it.
/// </para>
/// <para>
/// Full administration is the higher of the two values because it is the one thing on this side
/// that cannot be granted by accident: it is a membership of a protected group rather than an entry
/// somebody can add to a ruleset while meaning something else.
/// </para>
/// </remarks>
internal static class PhotoLibraryAudienceRule
{
    /// <summary>The refusal, kept apart from every other forbidden so a log names this feature.</summary>
    public const string ForbiddenCode = "photo_library.forbidden";

    public static bool MayRead(AccessContext ctx, PhotoLibraryAudience audience)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        return audience == PhotoLibraryAudience.SignedIn || ctx.IsFullAdmin;
    }
}
