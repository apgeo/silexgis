// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Messaging;

/// <summary>
/// A way of reaching a person. Stored as smallint on the template row; values are part of the
/// schema contract — do not renumber.
/// </summary>
public enum MessageChannel : short
{
    Email = 0,

    Sms = 1,
}
