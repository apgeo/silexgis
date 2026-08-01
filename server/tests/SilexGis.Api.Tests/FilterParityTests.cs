// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Domain;
using SilexGis.Domain.Permissions;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// EF ↔ SQL parity of the two security twins over the feature supertype: the read
/// visibility filter (<c>Features.VisibleTo</c> vs
/// <c>PermissionSql.FeatureVisibleToFragment</c>) and the exact-location rule
/// (<c>FeatureProtection.ExactViewIdsAsync</c> vs <c>PermissionSql.ExactViewFragment</c>),
/// each evaluated for five caller contexts — owner, teammate, stranger, ACL-Read grantee
/// and ViewExactLocation grantee — over one seeded matrix of visibilities, team
/// bindings, ACL grants and a protected containment chain. The twins may never diverge:
/// a mismatch is a security bug, not a flake.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FilterParityTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;   // Editor, owns every seeded feature
    private HttpClient manager = null!; // Manager, creates the team
    private Guid ownerId;
    private Guid teammateId;
    private Guid strangerId;
    private Guid aclReaderId;
    private Guid velGranteeId;
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    // The seeded matrix (all owned by the owner).
    private Guid cavePrivate;      // private, no grants
    private Guid caveAuth;         // authenticated
    private Guid caveTeam;         // team visibility, bound to the team
    private Guid caveAclOnly;      // private + ACL Read grant to aclReader
    private Guid areaProt;         // protected karst area + ACL Read|VEL grant to velGrantee
    private Guid caveChain;        // protected cave INSIDE areaProt (two protected roots)
    private Guid entranceChain;
    private Guid caveProt;         // protected cave, no parent + ACL Read|VEL grant to velGrantee
    private Guid entranceProt;
    private Guid caveTeamProt;     // protected cave, team visibility (members implicitly hold VEL)
    private Guid entranceTeamProt;
    private Guid[] candidateIds = [];

    public FilterParityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"par-own-{suffix}@t.local");
        teammateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"par-team-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"par-str-{suffix}@t.local");
        aclReaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"par-read-{suffix}@t.local");
        velGranteeId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"par-vel-{suffix}@t.local");
        _ = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Manager, $"par-mgr-{suffix}@t.local");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
            caveTypeId = await db.CaveTypes.Select(t => t.Id).FirstAsync();
            entranceTypeId = await db.EntranceTypes.Select(t => t.Id).FirstAsync();
            karstAreaTypeId = await db.FeatureTypes.Where(t => t.Code == "karst_area").Select(t => t.Id).FirstAsync();
        }

        owner = await AuthHelper.BearerClientAsync(factory, $"par-own-{suffix}@t.local");
        manager = await AuthHelper.BearerClientAsync(factory, $"par-mgr-{suffix}@t.local");

        // A team with the owner (so team-bound rows can be created) and the teammate.
        var teamId = await CreateTeamAsync();
        await AddMemberAsync(teamId, ownerId);
        await AddMemberAsync(teamId, teammateId);

        cavePrivate = await CreateCaveAsync("Par Private", "private");
        caveAuth = await CreateCaveAsync("Par Auth", "authenticated");
        caveTeam = await CreateCaveAsync("Par Team", "team", teamId: teamId);
        caveAclOnly = await CreateCaveAsync("Par AclOnly", "private");
        areaProt = await CreateProtectedAreaAsync("Par Area");
        caveChain = await CreateCaveAsync("Par Chain", "authenticated", locationProtected: true, parentId: areaProt);
        entranceChain = await AddEntranceAsync(caveChain);
        caveProt = await CreateCaveAsync("Par Prot", "authenticated", locationProtected: true);
        entranceProt = await AddEntranceAsync(caveProt);
        caveTeamProt = await CreateCaveAsync("Par TeamProt", "team", locationProtected: true, teamId: teamId);
        entranceTeamProt = await AddEntranceAsync(caveTeamProt);

        await ReplaceFeatureAclAsync(caveAclOnly, [(aclReaderId, ObjectPermission.Read)]);
        await ReplaceFeatureAclAsync(areaProt,
            [(velGranteeId, ObjectPermission.Read | ObjectPermission.ViewExactLocation)]);
        await ReplaceFeatureAclAsync(caveProt,
            [(velGranteeId, ObjectPermission.Read | ObjectPermission.ViewExactLocation)]);

        candidateIds =
        [
            cavePrivate, caveAuth, caveTeam, caveAclOnly, areaProt, caveChain,
            entranceChain, caveProt, entranceProt, caveTeamProt, entranceTeamProt,
        ];
    }

    [Fact]
    public async Task Read_visibility_filter_agrees_between_ef_and_sql_for_every_caller()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();
        var visible = new Dictionary<string, List<Guid>>();

        foreach (var (name, user) in await CallersAsync(db))
        {
            var efIds = await db.Features.VisibleTo(user, db.ObjectAcls)
                .Where(f => candidateIds.Contains(f.Id))
                .Select(f => f.Id).OrderBy(id => id).ToListAsync();

            var (fragment, parameters) = PermissionSql.FeatureVisibleToFragment(user, "f");
            parameters.Add("candidate_ids", PermissionSql.UuidArray(candidateIds));
            var sqlIds = (await connection.QueryAsync<Guid>(
                    $"SELECT f.id FROM features f WHERE f.deleted_at IS NULL" +
                    $" AND f.id = ANY(@candidate_ids) AND {fragment}",
                    parameters))
                .OrderBy(id => id).ToList();

            sqlIds.ShouldBe(efIds, $"visibility parity broke for caller '{name}'");
            visible[name] = efIds;
        }

        // Anchors keeping the parity meaningful (both sides agreeing on nonsense would
        // still be parity): each layer of the filter admits and denies as specified.
        visible["owner"].ShouldBe(candidateIds.OrderBy(id => id).ToList());
        visible["teammate"].ShouldContain(caveTeam);
        visible["teammate"].ShouldContain(caveTeamProt);
        visible["teammate"].ShouldNotContain(cavePrivate);
        visible["teammate"].ShouldNotContain(caveAclOnly);
        visible["stranger"].ShouldContain(caveAuth);
        visible["stranger"].ShouldNotContain(cavePrivate);
        visible["stranger"].ShouldNotContain(caveTeam);
        visible["stranger"].ShouldNotContain(caveAclOnly);
        visible["acl-reader"].ShouldContain(caveAclOnly);
        visible["acl-reader"].ShouldNotContain(cavePrivate);
        visible["vel-grantee"].ShouldNotContain(cavePrivate);
    }

    [Fact]
    public async Task Exact_view_rule_agrees_between_ef_and_sql_for_every_caller()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var protection = scope.ServiceProvider.GetRequiredService<FeatureProtection>();
        var connection = db.Database.GetDbConnection();
        var exact = new Dictionary<string, List<Guid>>();

        foreach (var (name, user) in await CallersAsync(db))
        {
            var domainIds = (await protection.ExactViewIdsAsync(user, candidateIds))
                .OrderBy(id => id).ToList();

            // The exact-view fragment consumes the same parameter set as the visibility
            // fragment; only the SQL text differs.
            var (_, parameters) = PermissionSql.FeatureVisibleToFragment(user, "f");
            parameters.Add("candidate_ids", PermissionSql.UuidArray(candidateIds));
            var sqlIds = (await connection.QueryAsync<Guid>(
                    $"SELECT f.id FROM features f WHERE f.deleted_at IS NULL" +
                    $" AND f.id = ANY(@candidate_ids) AND {PermissionSql.ExactViewFragment("f")}",
                    parameters))
                .OrderBy(id => id).ToList();

            sqlIds.ShouldBe(domainIds, $"exact-view parity broke for caller '{name}'");
            exact[name] = domainIds;
        }

        // Anchors: the row owner always sees exactly; team members implicitly hold
        // ViewExactLocation on team-bound roots; an explicit grant opens exactly the
        // granted root's subtree — and a chain under TWO protected roots stays closed
        // until every root is granted; strangers get nothing protected.
        exact["owner"].ShouldBe(candidateIds.OrderBy(id => id).ToList());
        exact["teammate"].ShouldContain(caveTeamProt);
        exact["teammate"].ShouldContain(entranceTeamProt);
        exact["teammate"].ShouldNotContain(caveChain);
        exact["teammate"].ShouldNotContain(entranceChain);
        exact["teammate"].ShouldNotContain(entranceProt);
        exact["stranger"].ShouldNotContain(areaProt);
        exact["stranger"].ShouldNotContain(caveChain);
        exact["stranger"].ShouldNotContain(entranceChain);
        exact["stranger"].ShouldNotContain(caveProt);
        exact["stranger"].ShouldNotContain(entranceProt);
        exact["stranger"].ShouldNotContain(caveTeamProt);
        exact["stranger"].ShouldNotContain(entranceTeamProt);
        exact["acl-reader"].ShouldNotContain(caveProt);
        exact["acl-reader"].ShouldNotContain(entranceProt);
        exact["vel-grantee"].ShouldContain(areaProt);
        exact["vel-grantee"].ShouldContain(caveProt);
        exact["vel-grantee"].ShouldContain(entranceProt);
        // The grant on the outer area root alone never opens the inner protected cave.
        exact["vel-grantee"].ShouldNotContain(caveChain);
        exact["vel-grantee"].ShouldNotContain(entranceChain);
    }

    // ---- seeding helpers ----

    private async Task<Guid> CreateTeamAsync()
    {
        var response = await manager.PostAsJsonAsync("/api/v1/teams/", new
        {
            name = $"Parity Team {Guid.NewGuid():N}"[..30],
            description = (string?)null,
            website = (string?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task AddMemberAsync(Guid teamId, Guid userId)
    {
        (await manager.PostAsJsonAsync($"/api/v1/teams/{teamId}/members", new
        {
            userId,
            role = "member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateCaveAsync(
        string name,
        string visibility,
        bool locationProtected = false,
        Guid? parentId = null,
        Guid? teamId = null)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility,
            locationProtected,
            parentId,
            teamId,
            explorationStatus = "Unknown",
            isShowCave = false,
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> CreateProtectedAreaAsync(string name)
    {
        var response = await owner.PostAsJsonAsync("/api/v1/features", new
        {
            kind = "generic",
            name = $"{name} {Guid.NewGuid():N}"[..40],
            featureTypeId = karstAreaTypeId,
            geometry = new
            {
                type = "Polygon",
                coordinates = new[]
                {
                    new[]
                    {
                        new[] { 25.40, 45.50 }, new[] { 25.55, 45.50 }, new[] { 25.55, 45.62 },
                        new[] { 25.40, 45.62 }, new[] { 25.40, 45.50 },
                    },
                },
            },
            locationProtected = true,
            visibility = "authenticated",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> AddEntranceAsync(Guid caveId)
    {
        var response = await owner.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
        {
            entranceTypeId,
            isMain = true,
            geom = new { type = "Point", coordinates = new[] { 25.44721, 45.53127 } },
            positionQuality = "Gps",
        });
        var payload = await response.Content.ReadAsStringAsync();
        response.StatusCode.ShouldBe(HttpStatusCode.Created, payload);
        return JsonDocument.Parse(payload).RootElement.GetProperty("id").GetGuid();
    }

    private async Task ReplaceFeatureAclAsync(Guid featureId, (Guid UserId, ObjectPermission Permissions)[] entries)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/acl", new
        {
            entries = entries.Select(e => new
            {
                subjectKind = "user",
                subjectId = e.UserId,
                permissions = e.Permissions.ToString().Replace(" ", string.Empty),
            }).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The five caller contexts, with team memberships loaded from the database.</summary>
    private async Task<(string Name, UserContext User)[]> CallersAsync(SilexGisDbContext db)
    {
        async Task<UserContext> ContextOf(Guid userId) => new(
            userId,
            new HashSet<string>(),
            await db.TeamMembers.Where(m => m.UserId == userId).ToDictionaryAsync(m => m.TeamId, m => m.Role));

        return
        [
            ("owner", await ContextOf(ownerId)),
            ("teammate", await ContextOf(teammateId)),
            ("stranger", await ContextOf(strangerId)),
            ("acl-reader", await ContextOf(aclReaderId)),
            ("vel-grantee", await ContextOf(velGranteeId)),
        ];
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
