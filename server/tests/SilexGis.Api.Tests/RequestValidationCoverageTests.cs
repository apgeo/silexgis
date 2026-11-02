// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;

namespace SilexGis.Api.Tests;

/// <summary>
/// Every route that reads a JSON body runs a validator over it. The validation filter passes a
/// body through untouched when no validator is registered for its type, and a route that never
/// calls the filter is simply unvalidated, so neither mistake fails any request-level test: the
/// route goes on accepting whatever binds, and a member left out arrives as a null the handler
/// dereferences. These tests walk the endpoint table instead, so a new route is held to the rule
/// the day it is mapped.
/// </summary>
/// <remarks>
/// The routes that do not meet the rule yet are listed by name below, each with what is missing.
/// The list is the debt as it stood when the walk was written; it may shrink and never grow, and
/// an entry that has come right fails the walk so it is taken off.
/// </remarks>
public sealed class RequestValidationCoverageTests : IClassFixture<PostgresFixture>, IDisposable
{
    /// <summary>
    /// Routes reading a JSON body with no validation filter, as "METHOD pattern". Each one is a
    /// known gap, kept here so the walk can guard every other route; the fix is a validator class
    /// and a WithValidation call on the route, after which the entry is removed.
    /// </summary>
    private static readonly HashSet<string> KnownUnvalidated = new(StringComparer.Ordinal)
    {
        // Adding pictures to an album.
        "POST /api/v1/albums/{id:guid}/items",
        // Running and resolving a filter document; its binding refuses a malformed shape, but no
        // validator bounds it.
        "POST /api/v1/filters/query",
        "POST /api/v1/filters/resolve",
        // Re-importing a geodata file with new options.
        "POST /api/v1/geofiles/{id:guid}/reimport",
    };

    /// <summary>
    /// Routes that call WithValidation for a body type no validator class exists for, so the filter
    /// passes every body through. Each is a known gap: the fix is the missing validator class.
    /// </summary>
    private static readonly HashSet<string> KnownWithoutValidator = new(StringComparer.Ordinal)
    {
        "POST /api/v1/albums/{id:guid}/reorder",
        "PUT /api/v1/trip-logs/{id:guid}/expedition",
        "PUT /api/v1/users/{id:guid}",
        "PUT /api/v1/users/{id:guid}/upload-review",
    };

    private readonly SilexGisApiFactory factory;

    public RequestValidationCoverageTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    [Fact]
    public void Every_route_reading_a_json_body_runs_the_validation_filter()
    {
        var unvalidated = BodyRoutes()
            .Where(r => r.Validated is null)
            .Select(r => r.Name)
            .ToHashSet(StringComparer.Ordinal);

        var newGaps = unvalidated.Except(KnownUnvalidated).Order().ToList();
        var closed = KnownUnvalidated.Except(unvalidated).Order().ToList();

        newGaps.ShouldBeEmpty(
            "these routes read a JSON body without WithValidation<T>(); add a validator and the call:\n"
            + string.Join("\n", newGaps));
        closed.ShouldBeEmpty(
            "these routes are validated now (or gone); take them off the known-gap list:\n"
            + string.Join("\n", closed));
    }

    [Fact]
    public void Every_validation_filter_checks_the_body_its_route_reads_and_has_a_validator()
    {
        using var scope = factory.Services.CreateScope();
        var wrong = new List<string>();
        var withoutValidator = new HashSet<string>(StringComparer.Ordinal);

        foreach (var route in BodyRoutes().Where(r => r.Validated is not null))
        {
            if (route.Validated != route.BodyType)
            {
                wrong.Add($"{route.Name}: validates {route.Validated!.Name} but reads {route.BodyType.Name}");
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(route.BodyType);
            if (scope.ServiceProvider.GetService(validatorType) is null)
            {
                withoutValidator.Add(route.Name);
                if (!KnownWithoutValidator.Contains(route.Name))
                {
                    wrong.Add($"{route.Name}: no IValidator<{route.BodyType.Name}> is registered, so the filter passes every body");
                }
            }
        }

        wrong.ShouldBeEmpty(string.Join("\n", wrong));
        var closed = KnownWithoutValidator.Except(withoutValidator).Order().ToList();
        closed.ShouldBeEmpty(
            "these routes have their validator now (or are gone); take them off the known-gap list:\n"
            + string.Join("\n", closed));
    }

    [Fact]
    public void The_walk_sees_the_routes_it_is_meant_to_judge()
    {
        // The two tests above pass on an empty table, so this one proves the walk finds body routes
        // at all and recognises a validated one: the sign-in route is validated, the known-gap list
        // names routes it is not.
        var routes = BodyRoutes().ToList();
        routes.Count.ShouldBeGreaterThan(50);
        routes.ShouldContain(r => r.Name == "POST /api/v1/auth/login" && r.Validated != null);
    }

    private IEnumerable<BodyRoute> BodyRoutes()
    {
        var endpoints = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints;
        foreach (var endpoint in endpoints.OfType<RouteEndpoint>())
        {
            var accepts = endpoint.Metadata.GetMetadata<IAcceptsMetadata>();
            if (accepts?.RequestType is not { } bodyType
                || !accepts.ContentTypes.Any(c => c.Contains("json", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? [];
            foreach (var method in methods)
            {
                yield return new BodyRoute(
                    $"{method} {endpoint.RoutePattern.RawText}",
                    bodyType,
                    endpoint.Metadata.GetMetadata<ValidatedBodyMetadata>()?.RequestType);
            }
        }
    }

    private sealed record BodyRoute(string Name, Type BodyType, Type? Validated);

    public void Dispose() => factory.Dispose();
}
