// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Storage;

namespace SilexGis.Domain.Entities;

/// <summary>
/// The last level of one storage ceiling that administrators were told about, or that it fell
/// back to. One row per <see cref="StorageLimitKind"/>, written by the storage check.
/// </summary>
/// <remarks>
/// It exists so a crossing is reported once rather than on every pass: a store that sits at 80 %
/// for a month produces one notice, not one an hour. A fall back is recorded too, silently, so
/// that climbing past the same threshold again — after somebody freed space and it filled up
/// again — is a new crossing and is reported again.
/// </remarks>
public class StorageAlertState
{
    /// <summary>Which ceiling this is the state of; the row's key.</summary>
    public StorageLimitKind LimitKind { get; set; }

    /// <summary>The level last reported, or last fallen back to.</summary>
    public StorageLevel Level { get; set; }

    /// <summary>When <see cref="Level"/> last changed.</summary>
    public DateTimeOffset ChangedAt { get; set; }
}
