// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SilexGis.Domain;
using SilexGis.Infrastructure.Documents;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Auth;

public enum DemoSeedOutcome
{
    /// <summary>The dataset is present: it was written, or topped up.</summary>
    Seeded,

    /// <summary>There is no administrator to own the dataset. Nothing was written.</summary>
    NoAdministrator,

    /// <summary>
    /// The database holds accounts or content the dataset did not create, and the operator did not
    /// say that is known. Nothing was written.
    /// </summary>
    RefusedRealData,
}

/// <summary>
/// `seed-demo`: loads the demonstration dataset, owned by the first administrator. It refuses on
/// a database that holds accounts or content it did not create unless
/// <see cref="AlongsideRealDataSwitch"/> is given, because the dataset attaches its trips, links
/// and texts to whatever caves, cabinets and documents it finds and publishes a share link with a
/// token printed in the documentation.
/// </summary>
public static class DemoSeedCommand
{
    /// <summary>The explicit "this installation holds real data and I know" switch.</summary>
    public const string AlongsideRealDataSwitch = "--alongside-real-data";

    /// <summary>The process exit code of a refusal for real data (a missing administrator exits 1).</summary>
    public const int RefusedExitCode = 2;

    public sealed record Result(DemoSeedOutcome Outcome, ForeignData? Foreign, string? OwnerEmail);

    public static async Task<Result> RunAsync(
        IServiceProvider services, IReadOnlyCollection<string> args, CancellationToken ct = default)
    {
        var db = services.GetRequiredService<SilexGisDbContext>();

        // The oldest administrator, not whichever the role lookup happens to return first: with
        // several administrators the owner must not change from one run to the next.
        var adminRoleId = await db.Roles.Where(r => r.Name == GlobalRoles.Admin).Select(r => r.Id).FirstOrDefaultAsync(ct);
        var owner = await (from userRole in db.UserRoles
                           join user in db.Users on userRole.UserId equals user.Id
                           where userRole.RoleId == adminRoleId
                           orderby user.CreatedAt, user.Id
                           select new { user.Id, user.Email })
            .FirstOrDefaultAsync(ct);
        if (owner is null)
        {
            return new Result(DemoSeedOutcome.NoAdministrator, null, null);
        }

        if (!args.Contains(AlongsideRealDataSwitch))
        {
            var bootstrapEmail = services.GetRequiredService<IOptions<AdminBootstrapOptions>>().Value.Email;
            var foreign = await RealDataGuard.ForDemoAsync(db, bootstrapEmail, ct);
            if (foreign.Any)
            {
                return new Result(DemoSeedOutcome.RefusedRealData, foreign, owner.Email);
            }
        }

        await DemoSeeder.SeedAsync(
            db,
            owner.Id,
            services.GetRequiredService<DocumentWriteService>(),
            services.GetRequiredService<IFileStore>(),
            ct);
        return new Result(DemoSeedOutcome.Seeded, null, owner.Email);
    }
}
