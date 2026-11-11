// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace SilexGis.Api.Common;

/// <summary>
/// Telling a second request about the same thing, arriving at the same moment, from a defect.
/// </summary>
/// <remarks>
/// A handler that checks "is this taken?" and then writes leaves a window in which another
/// request passes the same check; the unique index is what finally refuses the second write,
/// and unhandled that refusal is an uncoded 500. Matched on the index by name rather than on the
/// error code alone, so that a different unique violation arriving through the same write — a
/// defect somewhere else — is not quietly answered as the expected refusal.
/// </remarks>
public static class UniqueViolations
{
    /// <summary>Whether the failed save was refused by one of the named unique indexes.</summary>
    public static bool IsOn(DbUpdateException e, params string[] indexes) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
        && violation.ConstraintName is { } name
        && Array.IndexOf(indexes, name) >= 0;
}
