// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Auth;

namespace SilexGis.Api.Common;

/// <summary>
/// Parses the enum values that appear in route segments.
/// </summary>
/// <remarks>
/// Minimal APIs bind an enum route parameter with a case-sensitive <c>Enum.TryParse</c>, which
/// does not match this API's JSON contract: enums are serialised camelCase, so a client that read
/// <c>"email"</c> out of a response and put it back in a URL would fail to bind — and fail as an
/// unhandled 500 rather than a refusal. Route segments are therefore taken as strings and parsed
/// here, case-insensitively, matching what the rest of the contract says these values look like.
/// </remarks>
public static class RouteEnums
{
    public static bool TryParseTwoFactorMethod(string? value, out TwoFactorMethod method) =>
        Enum.TryParse(value, ignoreCase: true, out method) && Enum.IsDefined(method);
}
