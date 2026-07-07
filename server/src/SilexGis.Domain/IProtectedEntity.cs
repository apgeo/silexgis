// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>
/// RLS-ready access-control columns carried by every protected table.
/// The permission evaluator and visibility filters operate on these.
/// </summary>
public interface IProtectedEntity
{
    Guid OwnerUserId { get; set; }

    Guid? TeamId { get; set; }

    Visibility Visibility { get; set; }
}
