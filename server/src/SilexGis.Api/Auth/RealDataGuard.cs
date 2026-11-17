// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Auth;

/// <summary>How much of an installation's content and accounts a development aid did not create.</summary>
public sealed record ForeignData(int Accounts, int Caves, int Trips, int Documents)
{
    public bool Any => Accounts + Caves + Trips + Documents > 0;

    /// <summary>Counts only — what the refusal says must never quote the data it found.</summary>
    public string Describe() =>
        $"{Accounts} account(s), {Caves} cave(s), {Trips} trip(s) and {Documents} document(s)";
}

/// <summary>
/// The one test both development aids that write into a database — the demonstration dataset and
/// the announced test logins — put to it before they touch it: does it hold accounts or content
/// they did not create? Either aid is harmless on a scratch installation and harmful on one that
/// holds a club's real caves and members: the demonstration dataset ties its trips and texts to
/// whatever caves and cabinets it finds, and the test logins print a full administrator's
/// password on the sign-in page. A sentence in the guide saying "not on a real installation" is
/// not a guard; an environment file copied from a test host to a real one is exactly how this
/// goes wrong. So each refuses, unless the operator names the explicit switch that says "this
/// installation holds real data and I know".
/// </summary>
/// <remarks>
/// The accounts these aids may meet without that being a sign of real use are the ones the
/// development tooling itself makes: the bootstrap administrator the installation was configured
/// with, the three test logins and the development sync member. Any other account is somebody's.
/// The bootstrap administrator's content is another matter — see each check.
/// </remarks>
public static class RealDataGuard
{
    /// <summary>The prefix every demonstration cave's identification code carries.</summary>
    public const string DemoCavePrefix = "DEMO-";

    /// <summary>The prefix every demonstration trip's title carries.</summary>
    public const string DemoTripPrefix = "Demo:";

    public static IReadOnlyList<string> BuiltInEmails(string? bootstrapAdminEmail) =>
    [
        .. string.IsNullOrWhiteSpace(bootstrapAdminEmail) ? Array.Empty<string>() : [bootstrapAdminEmail],
        .. TestLoginSeeder.Accounts.Select(a => a.Email),
        SpeleoLocDevSeeder.MemberEmail,
    ];

    /// <summary>
    /// For the test logins: accounts that are not built in, and content that none of the
    /// development aids made. Content owned by the three test logins or the development sync
    /// member is theirs by ownership — a test installation is one where visitors signed in as the
    /// test accounts create caves and trips of their own, and those are no reason to stop
    /// announcing them. Content owned by anybody else, the bootstrap administrator included, is
    /// judged by what made it: the demonstration dataset's markings, or a test-data run's
    /// registry. The bootstrap administrator cannot be exempt by ownership, because on a small
    /// club's installation that one account is the one that enters the whole real register.
    /// </summary>
    public static async Task<ForeignData> ForTestLoginsAsync(
        SilexGisDbContext db, string? bootstrapAdminEmail, CancellationToken ct = default)
    {
        var builtIn = await UserIdsAsync(db, BuiltInEmails(bootstrapAdminEmail), ct);
        var testOwned = await UserIdsAsync(db, BuiltInEmails(null), ct);
        var accounts = await db.Users.CountAsync(u => !builtIn.Contains(u.Id), ct);
        var caves = await db.Caves.IgnoreQueryFilters()
            .CountAsync(c => !testOwned.Contains(c.Feature.OwnerUserId)
                && (c.IdentificationCode == null || !c.IdentificationCode.StartsWith(DemoCavePrefix))
                && !db.TestDataRows.Any(r => r.TableName == "features" && r.RowId == c.Id.ToString()), ct);
        var trips = await db.TripLogs.IgnoreQueryFilters()
            .CountAsync(t => !testOwned.Contains(t.OwnerUserId)
                && !t.Title.StartsWith(DemoTripPrefix)
                && !db.TestDataRows.Any(r => r.TableName == "trip_logs" && r.RowId == t.Id.ToString()), ct);
        var documents = await db.Documents.IgnoreQueryFilters()
            .CountAsync(d => !testOwned.Contains(d.OwnerUserId)
                && !EF.Functions.ILike(d.Title, "%demo%")
                && !db.TestDataRows.Any(r => r.TableName == "documents" && r.RowId == d.Id.ToString()), ct);
        return new ForeignData(accounts, caves, trips, documents);
    }

    /// <summary>
    /// For the demonstration dataset: accounts that are not built in, and content that is not the
    /// dataset's own. Content is judged by the markings the dataset puts on what it writes — its
    /// caves' codes, its trips' titles, its documents' titles — because the dataset is owned by
    /// the bootstrap administrator, the same account that on a real installation enters the real
    /// register, so the owner cannot tell the two apart.
    /// </summary>
    public static async Task<ForeignData> ForDemoAsync(
        SilexGisDbContext db, string? bootstrapAdminEmail, CancellationToken ct = default)
    {
        var builtIn = await UserIdsAsync(db, BuiltInEmails(bootstrapAdminEmail), ct);
        var accounts = await db.Users.CountAsync(u => !builtIn.Contains(u.Id), ct);
        var caves = await db.Caves.IgnoreQueryFilters()
            .CountAsync(c => c.IdentificationCode == null || !c.IdentificationCode.StartsWith(DemoCavePrefix), ct);
        var trips = await db.TripLogs.IgnoreQueryFilters().CountAsync(t => !t.Title.StartsWith(DemoTripPrefix), ct);
        var documents = await db.Documents.IgnoreQueryFilters()
            .CountAsync(d => !EF.Functions.ILike(d.Title, "%demo%"), ct);
        return new ForeignData(accounts, caves, trips, documents);
    }

    private static async Task<List<Guid>> UserIdsAsync(
        SilexGisDbContext db, IReadOnlyList<string> emails, CancellationToken ct)
    {
        // Identity normalises addresses to upper case with the invariant culture.
        var normalized = emails.Select(e => e.ToUpperInvariant()).ToList();
        return await db.Users
            .Where(u => u.NormalizedEmail != null && normalized.Contains(u.NormalizedEmail))
            .Select(u => u.Id)
            .ToListAsync(ct);
    }
}
