// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Primitives;
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

    /// <summary>
    /// Sets the ETag response header from the row's current version.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A read answers with the version it found; a write answers with the version it produced.
    /// The second matters as much as the first: a write is checked against the version the caller
    /// last held, and if only a read could hand one out, every write would leave its caller one
    /// version behind the row they had just written, and a second save a moment later would be
    /// refused as a conflict the caller cannot see. A client that is handed the version with the
    /// answer can carry it on the next write without a read in between.
    /// </para>
    /// <para>
    /// <paramref name="resourcePath"/> is for an answer to a request made on another path than
    /// the resource's own — an action posted beside it, such as a state move. The path goes out as
    /// <c>Content-Location</c>, which is how a response says whose representation it carries, so
    /// that a client can file the version where its next write on that resource will look for it.
    /// A read, a full update and a creation leave it out: the request path, or the
    /// <c>Location</c> header of a 201, already names the resource.
    /// </para>
    /// </remarks>
    public static async Task EmitETagAsync(
        HttpContext http, SilexGisDbContext db, VersionedTable table, Guid id, CancellationToken ct,
        string? resourcePath = null)
    {
        var version = await ConcurrencySql.VersionAsync(db, table, id, ct);
        if (version is null)
        {
            return;
        }

        http.Response.Headers.ETag = $"\"{version}\"";
        if (resourcePath is not null)
        {
            http.Response.Headers.ContentLocation = resourcePath;
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

        if (Names(header, $"\"{await ConcurrencySql.VersionAsync(db, table, id, ct)}\""))
        {
            return null;
        }

        return ApiProblems.PreconditionFailed(
            "concurrency.version_mismatch",
            "The resource changed since it was loaded. Reload and reapply your edits.");
    }

    /// <summary>
    /// A weak entity tag over a value this application computed: a validator for an answer that
    /// means the same as the last one without being the same bytes.
    /// </summary>
    /// <remarks>
    /// Weak on purpose where it is used: a compressing proxy may pass a weak tag through as it is
    /// and must alter or drop a strong one, and an answer carrying a freshly signed address
    /// differs from the previous answer byte for byte while saying the same thing. The value must
    /// hold no comma and end in none of the coding suffixes, since a request's list of tags is
    /// split on the one and a proxy's suffix is cut off by the other; digits and hexadecimal are
    /// both safe.
    /// </remarks>
    public static string WeakETag(string value) => $"W/\"{value}\"";

    /// <summary>
    /// Whether the request's <c>If-None-Match</c> names <paramref name="currentTag"/> — that is,
    /// whether the caller already holds the answer about to be sent.
    /// </summary>
    /// <remarks>
    /// The same comparison a write's <c>If-Match</c> gets, for the same reason: the tag comes back
    /// as a proxy left it, weakened or with the coding it applied appended, and neither says the
    /// answer changed. A request without the header holds nothing and matches nothing.
    /// </remarks>
    public static bool MatchesIfNoneMatch(HttpContext http, string currentTag) =>
        Names(http.Request.Headers.IfNoneMatch, currentTag);

    /// <summary>
    /// Whether a conditional header — one tag, a list of them, or <c>*</c> — names the current tag.
    /// </summary>
    private static bool Names(StringValues header, string currentTag)
    {
        var current = Canonical(currentTag);
        foreach (var value in header)
        {
            // One header line may carry a list of tags. Ours are digits or hexadecimal, so
            // splitting on the comma cannot cut one in half.
            foreach (var candidate in (value ?? string.Empty).Split(','))
            {
                var trimmed = candidate.Trim();
                if (trimmed == "*" || Canonical(trimmed) == current)
                {
                    return true;
                }
            }
        }

        return false;
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
