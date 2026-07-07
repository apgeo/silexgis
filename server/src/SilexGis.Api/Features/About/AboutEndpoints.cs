// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Reflection;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;

namespace SilexGis.Api.Features.About;

public static class AboutEndpoints
{
    /// <summary>
    /// Anonymous by design: exposes name, version, license and source location
    /// (AGPL §13; on the anonymous allow-list, 03-api-spec.md §7).
    /// </summary>
    public static RouteGroupBuilder MapAboutEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/about", (IOptions<AboutOptions> about) =>
            {
                var version = typeof(AboutEndpoints).Assembly
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                    .InformationalVersion ?? "unknown";
                return TypedResults.Ok(new AboutDto("SilexGIS", version, "AGPL-3.0-or-later", about.Value.SourceUrl));
            })
            .AllowAnonymous()
            .WithTags("About")
            .WithSummary("Application name, version, license and source-code location.");

        return api;
    }
}

public sealed record AboutDto(string Name, string Version, string License, string SourceUrl);
