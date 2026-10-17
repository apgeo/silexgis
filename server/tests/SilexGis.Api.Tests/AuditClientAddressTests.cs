// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Common;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Audit rows record the address of the client whose request caused them — the real one behind
/// the installation's trusted proxies, never one a caller merely wrote into a header — and no
/// address for work no request is behind.
/// </summary>
/// <remarks>
/// The test server gives a request no peer address at all, so the host here is given a stand-in
/// that sets one from a test-only header before anything else runs: the address the network
/// connection would have carried. The forwarded-headers middleware and everything after it are
/// the application's own.
/// </remarks>
public sealed class AuditClientAddressTests : IAsyncLifetime, IDisposable, IClassFixture<PostgresFixture>
{
    private const string PeerHeader = "X-Test-Peer";

    private readonly SilexGisApiFactory factory;
    private HttpClient admin = null!;

    public AuditClientAddressTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(
            postgres.ConnectionString,
            configureServices: services => services.AddSingleton<IStartupFilter, PeerAddressStandIn>());

    public async ValueTask InitializeAsync()
    {
        var email = $"audit-ip-{Guid.NewGuid():N}@t.local";
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Admin, email);
        admin = await AuthHelper.BearerClientAsync(factory, email);
    }

    [Fact]
    public async Task A_write_over_http_records_the_address_it_came_from()
    {
        var id = await CreateClubAsync(peer: "203.0.113.7", forwardedFor: null);

        (await AddressOfCreationAsync(id)).ShouldBe(IPAddress.Parse("203.0.113.7"));
    }

    [Fact]
    public async Task Behind_a_trusted_proxy_the_forwarded_client_is_recorded_rather_than_the_proxy()
    {
        // A private-network peer is one of the installation's own proxies by default.
        var id = await CreateClubAsync(peer: "10.0.0.5", forwardedFor: "198.51.100.9");

        (await AddressOfCreationAsync(id)).ShouldBe(IPAddress.Parse("198.51.100.9"));
    }

    [Fact]
    public async Task A_forwarded_address_from_an_untrusted_peer_is_not_believed()
    {
        var id = await CreateClubAsync(peer: "203.0.113.7", forwardedFor: "198.51.100.9");

        (await AddressOfCreationAsync(id)).ShouldBe(IPAddress.Parse("203.0.113.7"));
    }

    [Fact]
    public async Task Work_no_request_is_behind_records_no_address()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var club = new CavingGroup { Name = $"Background club {Guid.NewGuid():N}", Slug = $"bg-{Guid.NewGuid():N}" };
        db.CavingGroups.Add(club);
        await db.SaveChangesAsync();

        (await AddressOfCreationAsync(club.Id)).ShouldBeNull();
    }

    /// <summary>
    /// The web host's caller and address replace the persistence layer's no-request defaults
    /// whichever of the two registration calls runs first. Before, the right one won only by being
    /// registered later; had that order changed, every audit row would have recorded no user and
    /// no address with every other test still green.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void The_request_answers_who_and_from_where_whatever_the_registration_order(bool persistenceFirst)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Db:ConnectionString"] = "Host=localhost;Database=unused",
            })
            .Build();
        var services = new ServiceCollection();

        if (persistenceFirst)
        {
            services.AddSilexGisPersistence(configuration);
            services.AddSilexGisAuth(configuration);
        }
        else
        {
            services.AddSilexGisAuth(configuration);
            services.AddSilexGisPersistence(configuration);
        }

        services.Where(d => d.ServiceType == typeof(ICurrentUser))
            .Select(d => d.ImplementationType)
            .ShouldBe([typeof(HttpContextCurrentUser)]);
        services.Where(d => d.ServiceType == typeof(IRequestOrigin))
            .Select(d => d.ImplementationType)
            .ShouldBe([typeof(HttpContextRequestOrigin)]);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    public void Dispose() => factory.Dispose();

    private async Task<Guid> CreateClubAsync(string peer, string? forwardedFor)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/caving-groups")
        {
            Content = JsonContent.Create(new
            {
                name = $"Address club {Guid.NewGuid():N}",
                description = (string?)null,
                website = (string?)null,
            }),
        };
        request.Headers.Add(PeerHeader, peer);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        var response = await admin.SendAsync(request);
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<IPAddress?> AddressOfCreationAsync(Guid clubId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var row = await db.AuditEntries.AsNoTracking().SingleAsync(a =>
            a.EntityType == nameof(CavingGroup) && a.EntityId == clubId.ToString() && a.Action == AuditActions.Created);
        return row.Ip;
    }

    /// <summary>Sets the connection's peer address from the test header, ahead of the whole pipeline.</summary>
    private sealed class PeerAddressStandIn : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, following) =>
            {
                if (context.Request.Headers.TryGetValue(PeerHeader, out var peer)
                    && IPAddress.TryParse(peer.ToString(), out var address))
                {
                    context.Connection.RemoteIpAddress = address;
                }

                await following(context);
            });
            next(app);
        };
    }
}
