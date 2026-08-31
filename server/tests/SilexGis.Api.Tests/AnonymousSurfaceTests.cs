// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

/// <summary>
/// The set of routes a caller with no credentials at all may reach, pinned as a whole.
/// </summary>
/// <remarks>
/// <para>
/// Everything under the versioned API refuses an unauthenticated caller by default, and each
/// exception is a deliberate act with a reason. Until this test existed the record of which
/// exceptions had been made was prose in a document, kept by hand, and by the time it was checked
/// it was missing four routes that had been live for months — because nothing anywhere compared
/// the two. So the comparison is made here, against the application's own routing table, and it
/// is an equality rather than a subset: a route that joins the anonymous surface fails this test
/// the day it is added, and so does one that quietly leaves it.
/// </para>
/// <para>
/// Scoped to the versioned API because that is the surface with a default to be an exception to.
/// The protocol endpoints, the health probes and the served description sit outside it and were
/// never inside the default-deny group, so listing them here would suggest they are exceptions to
/// a rule that does not reach them.
/// </para>
/// <para>
/// Read from the endpoint metadata rather than from the source, because that is the same place
/// the served description reads it from when it decides which operations to publish as needing no
/// token — so the two records cannot disagree about what the surface is.
/// </para>
/// <para>
/// Matched on the method as well as the pattern, and on the pattern exactly as the router keeps
/// it — route constraints included — so that renaming a route or widening the methods it answers
/// fails here rather than silently matching something else.
/// </para>
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class AnonymousSurfaceTests(PostgresFixture postgres) : IDisposable
{
    private readonly SilexGisApiFactory factory = new(postgres.ConnectionString);

    /// <summary>
    /// Every route under the versioned API that answers a caller carrying nothing, and why each
    /// one is allowed to. A line added here without a reason to say is a line that should not be
    /// added.
    /// </summary>
    private static readonly (string Method, string Route, string Why)[] Recorded =
    [
        // Signing in cannot require being signed in.
        ("POST", "/api/v1/auth/login", "the caller has no session yet, which is the point"),
        ("POST", "/api/v1/auth/logout", "ending a session that may already be gone"),
        ("POST", "/api/v1/auth/register", "creating the account, where the installation allows it"),
        ("POST", "/api/v1/auth/password/forgot", "asked precisely by somebody who cannot sign in"),
        ("POST", "/api/v1/auth/password/reset", "the emailed token is the whole claim"),
        ("POST", "/api/v1/auth/email/confirm", "the emailed token is the whole claim"),
        ("POST", "/api/v1/auth/email/resend", "the confirmation has not happened yet"),
        ("POST", "/api/v1/auth/2fa/send", "sent in the middle of signing in, before there is a session"),
        ("GET", "/api/v1/auth/config", "which sign-in methods to offer, needed to draw the sign-in page"),
        ("GET", "/api/v1/auth/external/{provider}", "begins a sign-in at an external provider"),
        ("GET", "/api/v1/auth/external/{provider}/callback", "the provider returns here, carrying its own proof"),

        // The application's own identity, which it is required to state.
        ("GET", "/api/v1/about", "name, version, licence and where the source is, which the licence requires be reachable"),

        // A token in the address is the credential, because the caller cannot send a header.
        ("GET", "/api/v1/files/{id:guid}/content", "loaded ambiently by the browser, which cannot attach a token header"),
        ("GET", "/api/v1/files/{id:guid}/thumbnail", "loaded ambiently by the browser, which cannot attach a token header"),
        ("GET", "/api/v1/files/{id:guid}/pages/{page:int}/render", "loaded ambiently by the browser, which cannot attach a token header"),
        ("GET", "/api/v1/files/{id:guid}/pages/{page:int}/text", "answers on the same terms as the picture of the same page"),

        // A token somebody was handed deliberately is the credential.
        ("POST", "/api/v1/notifications/unsubscribe", "the signed token in the message is the claim; stopping mail cannot need an account"),
        ("GET", "/api/v1/shared/features/{token}", "a link somebody chose to hand out; the token is the credential"),
        ("GET", "/api/v1/shared/views/{token:guid}", "a link somebody chose to hand out; the token is the credential"),
        ("GET", "/api/v1/public/albums/{token}", "a link somebody chose to hand out; the token is the credential"),

        // Published deliberately, with the response shaped so that reaching it discloses nothing else.
        ("GET", "/api/v1/public/photos", "pictures an administrator published, carrying no position"),
        ("GET", "/api/v1/public/qr/{code}", "a code printed on a cave label, answering only with this installation's name"),
    ];

    /// <summary>
    /// The recorded set and the routed set are the same set.
    /// </summary>
    /// <remarks>
    /// Deliberately not a count, and deliberately not a check that each recorded route is
    /// anonymous. Three of these are anonymous because the group they sit in is, so a route added
    /// to one of those groups is anonymous the moment it is written, with nothing at its own call
    /// site to notice — which is the way this surface actually grows.
    /// </remarks>
    [Fact]
    public void The_anonymous_surface_is_exactly_what_is_recorded()
    {
        var routed = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1", StringComparison.Ordinal) == true)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .SelectMany(
                e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods ?? ["*"],
                (e, method) => $"{method} {e.RoutePattern.RawText}")
            .ToHashSet(StringComparer.Ordinal);

        // If this ever reads zero the comparison below proves nothing, so it is stated as a fact.
        routed.Count.ShouldBeGreaterThan(0);

        var recorded = Recorded.Select(r => $"{r.Method} {r.Route}").ToHashSet(StringComparer.Ordinal);

        var added = routed.Except(recorded).Order(StringComparer.Ordinal).ToList();
        var gone = recorded.Except(routed).Order(StringComparer.Ordinal).ToList();

        added.ShouldBeEmpty(
            "these routes answer a caller with no credentials and are not in the recorded list — "
            + "widen the record deliberately, with the reason, or take the route back off the surface");
        gone.ShouldBeEmpty(
            "these routes are recorded as anonymous but are not — if that is intended, take them "
            + "out of the record so it keeps describing the surface");
    }

    /// <summary>
    /// And the default itself: everything under the versioned API that is not on that list
    /// refuses a caller carrying nothing. Asserted so the list above is a list of exceptions to a
    /// rule that is proved to exist, rather than a list standing on its own.
    /// </summary>
    [Fact]
    public void Everything_else_under_the_versioned_api_requires_authorization()
    {
        var guarded = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(e => e.RoutePattern.RawText?.StartsWith("/api/v1", StringComparison.Ordinal) == true)
            .Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is null)
            .ToList();

        guarded.Count.ShouldBeGreaterThan(100);
        guarded.ShouldAllBe(e => e.Metadata.GetMetadata<IAuthorizeData>() != null);
    }

    public void Dispose() => factory.Dispose();
}
