// SPDX-License-Identifier: AGPL-3.0-or-later
using OpenIddict.Abstractions;

namespace SilexGis.Api.Auth;

/// <summary>
/// Removes the sign-in server's dead rows — tokens that expired, were revoked or were redeemed,
/// and the authorizations they hung off — once they are old enough that nothing will ask about
/// them again.
/// </summary>
/// <remarks>
/// <para>
/// Every sign-in, every silent renewal and every refresh-token rotation writes rows, and nothing
/// else ever deletes one: a revoked or rotated token stays in the table for good. The sign-in
/// server's own prune is used rather than a hand-written delete, because which states count as
/// finished is its knowledge, not this application's.
/// </para>
/// <para>
/// Its threshold is on when a row was <em>created</em>, and a row is only removed if it is also
/// finished — a refresh token still valid and unexpired is kept however old it is. Fourteen days
/// keeps a recently revoked or rotated token recognisable for a fortnight, which is when someone
/// looking into an odd sign-in would want it; after that it is only weight.
/// </para>
/// <para>
/// Runs every six hours and never at start, so a restart loop cannot turn into a delete loop,
/// in the manner of the hourly notification retention pass.
/// </para>
/// </remarks>
public sealed class TokenStorePruneWorker(
    IServiceScopeFactory scopeFactory,
    TimeProvider clock,
    ILogger<TokenStorePruneWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(6);

    /// <summary>How long a finished token or authorization is kept, counted from its creation.</summary>
    public static readonly TimeSpan Kept = TimeSpan.FromDays(14);

    /// <summary>One pass: removes the finished rows created before <paramref name="now"/> less <see cref="Kept"/>.</summary>
    public static async Task PruneAsync(IServiceProvider services, DateTimeOffset now, CancellationToken ct)
    {
        var threshold = now - Kept;
        // Tokens first: an authorization is only finished once nothing still hangs off it.
        await services.GetRequiredService<IOpenIddictTokenManager>().PruneAsync(threshold, ct);
        await services.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync(threshold, ct);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, clock);
        while (await NextAsync(timer, stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                await PruneAsync(scope.ServiceProvider, clock.GetUtcNow(), stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                // A pass that throws must not end the schedule for the life of the process.
                logger.LogError(e, "Pruning finished sign-in tokens failed; will try again next pass");
            }
        }
    }

    private static async Task<bool> NextAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
