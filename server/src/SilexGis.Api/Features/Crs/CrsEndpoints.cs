// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using SilexGis.Api.Common;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Geodata;

namespace SilexGis.Api.Features.Crs;

/// <summary>
/// Coordinate reference system definitions, resolved from the PROJ database that ships with the
/// application rather than from a public web service.
///
/// <para>
/// This exists because the vendored 3D survey viewer resolves the CRS named in a Survex <c>.3d</c>
/// header by fetching <c>https://epsg.io/{code}.proj4</c> at parse time. That is an uncontrolled
/// outbound request from a self-hosted application, and on an installation with no route out it
/// fails — so a survey in any system the viewer does not hard-code (Romanian Stereo70, EPSG:31700,
/// is one) loses its georeferencing, or fails to load at all. The client rewrites that request to
/// this route; the response body is the plain PROJ.4 string the viewer expects, so nothing in the
/// viewer has to change.
/// </para>
/// </summary>
public static class CrsEndpoints
{
    /// <summary>
    /// Highest code accepted. EPSG codes are small positive integers — the registry's own range
    /// tops out in the tens of thousands — and the bound keeps a caller from walking an unbounded
    /// integer space against the definition cache.
    /// </summary>
    private const int MaxEpsgCode = 999_999;

    public static RouteGroupBuilder MapCrsEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/crs/{srid:int}.proj4", Proj4Async)
            .WithTags("Crs")
            .WithSummary("PROJ.4 definition of one EPSG code as plain text, resolved offline.");
        return api;
    }

    private static async Task<Results<ContentHttpResult, UnauthorizedHttpResult, ProblemHttpResult>> Proj4Async(
        int srid,
        IUserContextAccessor userAccessor,
        ICrsRegistry registry,
        HttpContext http,
        CancellationToken ct)
    {
        if (await userAccessor.GetAsync(ct) is null)
        {
            return TypedResults.Unauthorized();
        }

        if (srid <= 0 || srid > MaxEpsgCode)
        {
            return ApiProblems.BadRequest("crs.code_invalid", "An EPSG code is a positive integer.");
        }

        var proj4 = registry.Proj4(srid);
        if (proj4 is null)
        {
            return ApiProblems.NotFound("crs.not_found");
        }

        // A CRS definition never changes, so the answer can be held for as long as the caller
        // likes. Private rather than public because the request carries a bearer token, and a
        // shared cache must not key on a URL whose response was fetched with someone's credential.
        http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return TypedResults.Text(proj4, "text/plain");
    }
}
