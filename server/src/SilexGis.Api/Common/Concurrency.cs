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
    /// <summary>
    /// Suffixes a reverse proxy appends to an entity tag when it compresses the response body.
    /// nginx and Apache both do this, and the browser stores what it was given, so the tag that
    /// comes back on the next write is the mangled one.
    /// </summary>
    private static readonly string[] EncodingSuffixes = ["-gzip", "-br", "-deflate", "-zstd"];

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

        var current = Canonical($"\"{await ConcurrencySql.VersionAsync(db, table, id, ct)}\"");
        foreach (var value in header)
        {
            // One header line may carry a list of tags. Ours are digits, so splitting on the
            // comma cannot cut one in half.
            foreach (var candidate in (value ?? string.Empty).Split(','))
            {
                var trimmed = candidate.Trim();
                if (trimmed == "*" || Canonical(trimmed) == current)
                {
                    return null;
                }
            }
        }

        return ApiProblems.PreconditionFailed(
            "concurrency.version_mismatch",
            "The resource changed since it was loaded. Reload and reapply your edits.");
    }

    /// <summary>
    /// An entity tag reduced to the part this application issued, so that what a proxy did to it
    /// on the way out does not read as a change to the row.
    /// </summary>
    /// <remarks>
    /// Two things happen to a tag between here and the browser. A compressing reverse proxy
    /// appends the coding it applied — nginx turns <c>"1031"</c> into <c>"1031-gzip"</c> — because
    /// the compressed body really is a different sequence of bytes; and a cache may downgrade a
    /// strong tag to a weak one. Neither says anything about the row, but both come back on the
    /// next write, and comparing them literally refuses every edit made through the deployment
    /// this project ships: the response that carried the version was JSON, which that nginx
    /// compresses, while the write that replays it is not. The comparison is therefore made on
    /// the version itself, which is what it was ever about.
    /// </remarks>
    private static string Canonical(string tag)
    {
        var value = tag.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            value = value[1..^1];
        }

        foreach (var suffix in EncodingSuffixes)
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                return value[..^suffix.Length];
            }
        }

        return value;
    }
}
