// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// Ambient identity of the caller, used by auditing (and later by the permission service).
/// The web host provides an HttpContext-backed implementation once authentication lands;
/// until then a null-returning implementation is registered.
/// </summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}
