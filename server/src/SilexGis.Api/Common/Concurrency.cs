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
    /// Validates the If-Match precondition against the row's current version. Returns a 412
    /// Problem on mismatch. When <paramref name="required"/> is set (the /api/v1 contract for
    /// entities edited through a loaded detail view), a missing header yields 428; otherwise a
    /// missing header is allowed (last-write-wins) and returns null.
    /// </summary>
    public static async Task<ProblemHttpResult?> CheckIfMatchAsync(
        HttpContext http, SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct,
        bool required = false)
    {
        var header = http.Request.Headers.IfMatch;
        if (header.Count == 0)
        {
            return required
                ? ApiProblems.PreconditionRequired(
                    "concurrency.if_match_required",
                    "This resource requires an If-Match header carrying the version you last loaded.")
                : null;
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
