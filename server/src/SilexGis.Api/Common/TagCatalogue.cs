// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Api.Common;

/// <summary>
/// Whether the caller may be told tags at all: Read over the tag domain, asked of no tag in
/// particular. The tag list, and every other surface that names tags without reading the
/// catalogue itself, defer to this one definition — so an installation that tightens the right
/// finds tag names withheld everywhere at once rather than only where somebody remembered to ask.
/// </summary>
public static class TagCatalogue
{
    public static bool MayRead(AccessContext? ctx) =>
        AccessEvaluator.Decide(ctx, AccessDomain.Tags, AccessAction.Read, null).Allowed;
}
