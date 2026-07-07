// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Security.Claims;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Common;

/// <summary>Resolves the caller's user id from the request principal (bearer or cookie).</summary>
public sealed class HttpContextCurrentUser(IHttpContextAccessor accessor) : SilexGis.Domain.ICurrentUser
{
    public Guid? UserId
    {
        get
        {
            var principal = accessor.HttpContext?.User;
            var subject = principal?.FindFirstValue(Claims.Subject)
                ?? principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(subject, out var id) ? id : null;
        }
    }
}
