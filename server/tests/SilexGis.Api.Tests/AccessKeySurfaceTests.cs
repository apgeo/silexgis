// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain.Auth;

namespace SilexGis.Api.Tests;

/// <summary>
/// Pins the routes a personal access key opens. A key is pasted into a field device, so what it
/// opens is what anybody holding that device can do as its owner: the set is recorded here route by
/// route, and a route that starts accepting a key without being recorded fails this test.
/// </summary>
/// <remarks>
/// The declaration is per route or per group, and a group's declaration reaches every route later
/// added to it with nothing at the new route's call site to notice — which is how this surface
/// would actually grow. Every other route that needs an account refuses a key centrally; that
/// refusal is asserted with real requests in <see cref="AccessKeyTests"/>.
/// </remarks>
public sealed class AccessKeySurfaceTests(PostgresFixture postgres) : IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory = new(postgres.ConnectionString);

    /// <summary>Every route a survey-upload key opens, and why a device needs it.</summary>
    private static readonly (string Method, string Route, string Why)[] SurveyUpload =
    [
        ("GET", "/api/v1/me/", "the device shows which account its key acts as, and tests the key with it"),
        ("GET", "/api/v1/caves/", "the device searches for the cave a survey belongs to; rows are visibility-filtered and positions protected as for any caller"),
        ("GET", "/api/v1/survey-uploads/capabilities", "the device learns what the server accepts and whether it compiles before the caver starts"),
        ("GET", "/api/v1/survey-uploads/", "the device lists surveys it may import; only caves whose survey the caller may see with the exact location"),
        ("POST", "/api/v1/survey-uploads/", "the device opens an upload of one survey, naming or making its cave on the account's own rights"),
        ("GET", "/api/v1/survey-uploads/{id:guid}", "the device follows the processing run it started"),
        ("PUT", "/api/v1/survey-uploads/{id:guid}/files/{name}", "the device sends each declared file; Write on the cave"),
        ("POST", "/api/v1/survey-uploads/{id:guid}/complete", "the device says every file arrived, which queues the run; Write on the cave"),
        ("POST", "/api/v1/survey-uploads/{id:guid}/rerun", "the device runs a finished upload again with other outputs; Write on the cave"),
        ("DELETE", "/api/v1/survey-uploads/{id:guid}", "the device gives up a session still receiving; what arrived stays archived"),
    ];

    [Fact]
    public void The_routes_a_survey_upload_key_opens_are_exactly_what_is_recorded()
    {
        var routed = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetOrderedMetadata<AcceptsAccessKeyMetadata>()
                .Any(m => m.Purpose == AccessKeyPurpose.SurveyUpload))
            .SelectMany(
                e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"],
                (e, method) => $"{method} {e.RoutePattern.RawText}")
            .ToHashSet(StringComparer.Ordinal);

        routed.Count.ShouldBeGreaterThan(0);
        var recorded = SurveyUpload.Select(r => $"{r.Method} {r.Route}").ToHashSet(StringComparer.Ordinal);

        routed.Except(recorded).Order(StringComparer.Ordinal).ToList().ShouldBeEmpty(
            "these routes accept a survey-upload key and are not recorded — record them, with the reason, "
            + "or take the declaration back off");
        recorded.Except(routed).Order(StringComparer.Ordinal).ToList().ShouldBeEmpty(
            "these routes are recorded as accepting a survey-upload key but do not");
    }

    public void Dispose() => factory.Dispose();
}
