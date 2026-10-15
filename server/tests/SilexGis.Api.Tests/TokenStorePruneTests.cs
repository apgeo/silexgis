// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using Shouldly;
using SilexGis.Api.Auth;
using SilexGis.Api.Tests.Support;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace SilexGis.Api.Tests;

/// <summary>
/// The scheduled prune of the sign-in server's stores: what is finished and old goes, what is
/// live or recent stays. Rows are written through the sign-in server's own managers with the dates
/// a real history would have, and the pass the schedule runs is called directly.
/// </summary>
public sealed class TokenStorePruneTests(PostgresFixture postgres) : IDisposable, IClassFixture<PostgresFixture>
{
    private readonly SilexGisApiFactory factory = new(postgres.ConnectionString);

    [Fact]
    public async Task Old_expired_and_revoked_rows_go_while_live_and_recently_revoked_ones_stay()
    {
        var now = DateTimeOffset.UtcNow;
        var subject = Guid.NewGuid().ToString();
        var old = now - TokenStorePruneWorker.Kept - TimeSpan.FromDays(5);

        string expired, revoked, live, recentlyRevoked, finishedGrant, liveGrant;
        using (var scope = factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var grants = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();

            liveGrant = await GrantAsync(grants, subject, old, Statuses.Valid);
            finishedGrant = await GrantAsync(grants, subject, old, Statuses.Revoked);
            expired = await TokenAsync(tokens, subject, liveGrant, old, now - TimeSpan.FromDays(10), Statuses.Valid);
            revoked = await TokenAsync(tokens, subject, liveGrant, old, now + TimeSpan.FromDays(30), Statuses.Revoked);
            live = await TokenAsync(tokens, subject, liveGrant, old, now + TimeSpan.FromDays(30), Statuses.Valid);
            recentlyRevoked = await TokenAsync(
                tokens, subject, liveGrant, now - TimeSpan.FromDays(2), now + TimeSpan.FromDays(30), Statuses.Revoked);
        }

        using (var scope = factory.Services.CreateScope())
        {
            await TokenStorePruneWorker.PruneAsync(scope.ServiceProvider, now, CancellationToken.None);
        }

        using (var scope = factory.Services.CreateScope())
        {
            var tokens = scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>();
            var grants = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();

            (await tokens.FindByIdAsync(expired)).ShouldBeNull();
            (await tokens.FindByIdAsync(revoked)).ShouldBeNull();
            (await tokens.FindByIdAsync(live)).ShouldNotBeNull();
            (await tokens.FindByIdAsync(recentlyRevoked)).ShouldNotBeNull();
            (await grants.FindByIdAsync(finishedGrant)).ShouldBeNull();
            (await grants.FindByIdAsync(liveGrant)).ShouldNotBeNull();
        }
    }

    public void Dispose() => factory.Dispose();

    private static async Task<string> GrantAsync(
        IOpenIddictAuthorizationManager grants, string subject, DateTimeOffset created, string status)
    {
        var grant = await grants.CreateAsync(new OpenIddictAuthorizationDescriptor
        {
            Subject = subject,
            CreationDate = created,
            Status = status,
            Type = AuthorizationTypes.Permanent,
        });
        return (await grants.GetIdAsync(grant))!;
    }

    private static async Task<string> TokenAsync(
        IOpenIddictTokenManager tokens,
        string subject,
        string authorizationId,
        DateTimeOffset created,
        DateTimeOffset expires,
        string status)
    {
        var token = await tokens.CreateAsync(new OpenIddictTokenDescriptor
        {
            Subject = subject,
            AuthorizationId = authorizationId,
            CreationDate = created,
            ExpirationDate = expires,
            Status = status,
            Type = TokenTypeHints.RefreshToken,
        });
        return (await tokens.GetIdAsync(token))!;
    }
}
