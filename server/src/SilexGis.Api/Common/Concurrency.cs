// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Optimistic-concurrency plumbing over the xmin row version: single-resource GETs emit
/// an ETag; PUT/DELETE validate If-Match with 412 on mismatch. The header is enforced
/// when provided — clients that send what they last saw get lost-update protection; the
/// header becomes mandatory at the API freeze.
/// </summary>
public static class Concurrency
{
    /// <summary>Sets the ETag response header from the row's current version.</summary>
    public static async Task EmitETagAsync(
        HttpContext http, SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct)
    {
        var version = await ConcurrencySql.VersionAsync(db, table, id, ct);
        if (version is not null)
        {
            http.Response.Headers.ETag = $"\"{version}\"";
        }
    }

    /// <summary>
    /// Returns a 412 Problem when the request carries If-Match and it does not match the
    /// row's current version; null otherwise (including when no header was sent).
    /// </summary>
    public static async Task<ProblemHttpResult?> CheckIfMatchAsync(
        HttpContext http, SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct)
    {
        var header = http.Request.Headers.IfMatch;
        if (header.Count == 0)
        {
            return null;
        }

        var current = $"\"{await ConcurrencySql.VersionAsync(db, table, id, ct)}\"";
        foreach (var value in header)
        {
            if (value == "*" || value == current)
            {
                return null;
            }
        }

        return ApiProblems.PreconditionFailed(
            "concurrency.version_mismatch",
            "The resource changed since it was loaded. Reload and reapply your edits.");
    }
}
