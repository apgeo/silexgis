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
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Tests;

/// <summary>
/// Parity of the THREE synchronized forms of the access rule over the feature
/// supertype: the pure evaluator (<c>AccessEvaluator.Decide</c>), the EF filter
/// (<c>Features.VisibleTo</c>) and the Dapper fragment
/// (<c>AccessSql.FeatureVisibleToFragment</c>) — plus the exact-location pair
/// (<c>FeatureProtection.ExactViewIdsAsync</c> vs <c>AccessSql.ExactViewFragment</c>).
/// Evaluated for twelve caller archetypes over one seeded matrix covering every band of
/// the walk: object allow over subtree deny, feature-set allow and deny, deny-own,
/// deny-own∧kind, allow-own∧kind (the conjunction that must never widen to all∧kind),
/// the caving-group starter ruleset, read-time visibility inheritance, and a VEL deny on
/// one of two protected roots. The forms may never diverge: a mismatch is a security
/// bug, not a flake.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class FilterParityTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private HttpClient owner = null!;      // Editor, owns most of the matrix
    private HttpClient manager = null!;    // Manager, creates the caving group
    private HttpClient selfDenied = null!; // Editor with a deny-own∧kind entry on themselves
    private Guid ownerId;
    private Guid groupMateId;      // Viewer; member of the caving group (starter ruleset)
    private Guid strangerId;       // Viewer; nothing but All Users and the built-ins
    private Guid entryReaderId;    // Viewer; direct object-scope Read grant
    private Guid velGranteeId;     // Viewer; direct Read+VEL grants on two roots
    private Guid subtreeDeniedId;  // Viewer; subtree deny + object allow inside it
    private Guid setDeniedId;      // Viewer; feature-set deny
    private Guid setAllowedId;     // Viewer; feature-set allow opening a private member
    private Guid selfDeniedId;     // Editor; deny Read own∧cave
    private Guid ownDeniedId;      // Editor; deny Read own (unnarrowed)
    private Guid ownKindAllowedId; // Viewer; allow Read own∧cave — the widening catcher
    private Guid velDeniedId;      // Viewer; global VEL allow + object VEL deny on one root
    private long caveTypeId;
    private long entranceTypeId;
    private long karstAreaTypeId;

    // The seeded matrix (owned by the owner unless said otherwise).
    private Guid cavingGroupId;
    private Guid cavePrivate;         // private, no grants
    private Guid caveAuth;            // authenticated — also the feature-set member
    private Guid entranceAuth;        // its entrance: PRIVATE own row, readable only by inheritance (D1d)
    private Guid caveCavingGroup;     // caving-group visibility, bound to the group
    private Guid caveEntryOnly;       // private + direct object-scope Read entry for entryReader
    private Guid areaProt;            // protected karst area
    private Guid caveChain;           // protected cave INSIDE areaProt (two protected roots)
    private Guid entranceChain;
    private Guid caveProt;            // protected cave, no parent
    private Guid entranceProt;
    private Guid caveCavingGroupProt; // protected cave, group visibility (VEL via the starter ruleset)
    private Guid entranceCavingGroupProt;
    private Guid caveDenyOwn;         // authenticated, owned by selfDenied — their deny-own∧cave target
    private Guid entranceDenyOwn;
    private Guid caveOwnDeniedPriv;   // private, owned by ownDenied — deny-own beats ownership
    private Guid caveOwnKindPriv;     // private, owned by ownKindAllowed — their own∧cave target
    private Guid featureSetId;
    private Guid[] candidateIds = [];

    public FilterParityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        ownerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"par-own-{suffix}@t.local");
        selfDeniedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"par-self-{suffix}@t.local");
        groupMateId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"par-group-mate-{suffix}@t.local");
        strangerId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"par-str-{suffix}@t.local");
        entryReaderId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"par-read-{suffix}@t.local");
        velGranteeId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"par-vel-{suffix}@t.local");
        subtreeDeniedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"par-sub-{suffix}@t.local");
        setDeniedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"par-set-{suffix}@t.local");
        setAllowedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"par-seta-{suffix}@t.local");
        ownDeniedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Editor, $"par-ownd-{suffix}@t.local");
        ownKindAllowedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"par-ownk-{suffix}@t.local");
        velDeniedId = await AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"par-veld-{suffix}@t.local");
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
        selfDenied = await AuthHelper.BearerClientAsync(factory, $"par-self-{suffix}@t.local");

        // A caving group with the owner (so group-bound rows can be created) and the
        // groupMate. Creating it seeds the "«name» — members" starter ruleset, which is
        // where the groupMate's rights — VEL included — come from now.
        cavingGroupId = await CreateCavingGroupAsync();
        await AddMemberAsync(cavingGroupId, ownerId);
        await AddMemberAsync(cavingGroupId, groupMateId);

        cavePrivate = await CreateCaveAsync(owner, "Par Private", "private");
        caveAuth = await CreateCaveAsync(owner, "Par Auth", "authenticated");
        entranceAuth = await AddEntranceAsync(owner, caveAuth);
        caveCavingGroup = await CreateCaveAsync(owner, "Par CavingGroup", "cavingGroup", cavingGroupId: cavingGroupId);
        caveEntryOnly = await CreateCaveAsync(owner, "Par EntryOnly", "private");
        areaProt = await CreateProtectedAreaAsync("Par Area");
        caveChain = await CreateCaveAsync(owner, "Par Chain", "authenticated", locationProtected: true, parentId: areaProt);
        entranceChain = await AddEntranceAsync(owner, caveChain);
        caveProt = await CreateCaveAsync(owner, "Par Prot", "authenticated", locationProtected: true);
        entranceProt = await AddEntranceAsync(owner, caveProt);
        caveCavingGroupProt = await CreateCaveAsync(owner, "Par CavingGroupProt", "cavingGroup", locationProtected: true, cavingGroupId: cavingGroupId);
        entranceCavingGroupProt = await AddEntranceAsync(owner, caveCavingGroupProt);
        caveDenyOwn = await CreateCaveAsync(selfDenied, "Par DenyOwn", "authenticated");
        entranceDenyOwn = await AddEntranceAsync(selfDenied, caveDenyOwn);
        using (var ownDeniedClient = await AuthHelper.BearerClientAsync(factory, $"par-ownd-{suffix}@t.local"))
        {
            caveOwnDeniedPriv = await CreateCaveAsync(ownDeniedClient, "Par OwnDenied", "private");
        }

        // ownKindAllowed is a regular account and cannot author caves; ownership is a row
        // fact, so the owner authors it and the fact is set directly.
        caveOwnKindPriv = await CreateCaveAsync(owner, "Par OwnKind", "private");

        // Direct object-scope grants ride the per-object surface (the one-off-grant
        // shape of the entry model).
        await ReplaceAccessRulesAsync(caveEntryOnly, [(entryReaderId, AccessAction.Read)]);
        await ReplaceAccessRulesAsync(areaProt,
            [(velGranteeId, AccessAction.Read | AccessAction.ViewExactLocation)]);
        await ReplaceAccessRulesAsync(caveProt,
            [(velGranteeId, AccessAction.Read | AccessAction.ViewExactLocation)]);

        // Denies, subtree/set scopes and the narrowed conjunction have no write surface
        // in this batch — they are rules, seeded straight into storage.
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

            var set = new FeatureSet { Name = $"Par Set {suffix}", Slug = $"par-set-{suffix}" };
            featureSetId = set.Id;
            db.FeatureSets.Add(set);
            db.FeatureSetMembers.Add(new FeatureSetMember { FeatureSetId = set.Id, FeatureId = caveAuth });

            // A second set holding a PRIVATE row: the allow arm has to open something
            // that nothing else would.
            var privateSet = new FeatureSet { Name = $"Par Set P {suffix}", Slug = $"par-set-p-{suffix}" };
            db.FeatureSets.Add(privateSet);
            db.FeatureSetMembers.Add(new FeatureSetMember { FeatureSetId = privateSet.Id, FeatureId = cavePrivate });
            db.AccessEntries.Add(Direct(setAllowedId, AccessEffect.Allow, AccessAction.Read,
                AccessScopeKind.FeatureSet, scopeId: privateSet.Id));

            // subtreeDenied: "deny the whole protected area, but allow this one cave" —
            // the walk's signature case (object allow inside a denied subtree).
            db.AccessEntries.Add(Direct(subtreeDeniedId, AccessEffect.Deny, AccessAction.Read,
                AccessScopeKind.Subtree, scopeFeatureId: areaProt));
            db.AccessEntries.Add(Direct(subtreeDeniedId, AccessEffect.Allow, AccessAction.Read,
                AccessScopeKind.Object, scopeFeatureId: caveChain));

            // setDenied: a collection-level deny through set membership.
            db.AccessEntries.Add(Direct(setDeniedId, AccessEffect.Deny, AccessAction.Read,
                AccessScopeKind.FeatureSet, scopeId: set.Id));

            // selfDenied: deny Read on their OWN caves only (own∧kind conjunction).
            db.AccessEntries.Add(Direct(selfDeniedId, AccessEffect.Deny, AccessAction.Read,
                AccessScopeKind.Own, kind: FeatureKind.Cave));

            // ownDenied: the unnarrowed deny-own — beats the ownership built-in, leaves
            // everything the Editors allow reaches.
            db.AccessEntries.Add(Direct(ownDeniedId, AccessEffect.Deny, AccessAction.Read,
                AccessScopeKind.Own));

            // ownKindAllowed: allow Read own∧cave. The conjunction must ride the scope in
            // every form — flattened as all∧cave it would open other people's caves.
            db.AccessEntries.Add(Direct(ownKindAllowedId, AccessEffect.Allow, AccessAction.Read,
                AccessScopeKind.Own, kind: FeatureKind.Cave));

            // Ownership is the row fact under test; set it without an authoring flow.
            await db.Features.Where(f => f.Id == caveOwnKindPriv)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.OwnerUserId, ownKindAllowedId));

            // velDenied: global VEL, vetoed on one root by an object-level deny.
            db.AccessEntries.Add(Direct(velDeniedId, AccessEffect.Allow, AccessAction.ViewExactLocation,
                AccessScopeKind.All));
            db.AccessEntries.Add(Direct(velDeniedId, AccessEffect.Deny, AccessAction.ViewExactLocation,
                AccessScopeKind.Object, scopeFeatureId: areaProt));

            await db.SaveChangesAsync();
        }

        candidateIds =
        [
            cavePrivate, caveAuth, entranceAuth, caveCavingGroup, caveEntryOnly, areaProt,
            caveChain, entranceChain, caveProt, entranceProt, caveCavingGroupProt,
            entranceCavingGroupProt, caveDenyOwn, entranceDenyOwn, caveOwnDeniedPriv,
            caveOwnKindPriv,
        ];
    }

    [Fact]
    public async Task Read_filter_agrees_between_evaluator_ef_and_sql_for_every_caller()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();
        var facts = await FactsForAsync(db, candidateIds);
        var visible = new Dictionary<string, List<Guid>>();

        foreach (var (name, ctx) in await CallersAsync(db))
        {
            var efIds = await db.Features.VisibleTo(ctx, db.Features, db.FeatureSetMembers)
                .Where(f => candidateIds.Contains(f.Id))
                .Select(f => f.Id).OrderBy(id => id).ToListAsync();

            var (fragment, parameters) = AccessSql.FeatureVisibleToFragment(ctx, "f");
            parameters.Add("candidate_ids", AccessSql.UuidArray(candidateIds));
            var sqlIds = (await connection.QueryAsync<Guid>(
                    $"SELECT f.id FROM features f WHERE f.deleted_at IS NULL" +
                    $" AND f.id = ANY(@candidate_ids) AND {fragment}",
                    parameters))
                .OrderBy(id => id).ToList();

            var evaluatorIds = candidateIds
                .Where(id => AccessEvaluator.Decide(ctx, AccessDomain.Features, AccessAction.Read, facts[id]).Allowed)
                .OrderBy(id => id).ToList();

            sqlIds.ShouldBe(efIds, $"EF↔SQL visibility parity broke for caller '{name}'");
            evaluatorIds.ShouldBe(efIds, $"evaluator↔EF visibility parity broke for caller '{name}'");
            visible[name] = efIds;
        }

        // Anchors keeping the parity meaningful (three forms agreeing on nonsense would
        // still be parity): each band of the walk admits and denies as specified.
        visible["owner"].ShouldBe(candidateIds.OrderBy(id => id).ToList());
        visible["groupMate"].ShouldContain(caveCavingGroup);
        visible["groupMate"].ShouldContain(caveCavingGroupProt);
        visible["groupMate"].ShouldNotContain(cavePrivate);
        visible["groupMate"].ShouldNotContain(caveEntryOnly);
        // The D1d cascade: a private entrance row under an authenticated cave is read
        // through its ancestor chain — for everyone authenticated, not just members.
        visible["stranger"].ShouldContain(caveAuth);
        visible["stranger"].ShouldContain(entranceAuth);
        visible["stranger"].ShouldNotContain(cavePrivate);
        visible["stranger"].ShouldNotContain(caveCavingGroup);
        visible["stranger"].ShouldNotContain(caveEntryOnly);
        visible["entry-reader"].ShouldContain(caveEntryOnly);
        visible["entry-reader"].ShouldNotContain(cavePrivate);
        visible["vel-grantee"].ShouldNotContain(cavePrivate);
        // Object allow inside a denied subtree: the cave decides at Object level, its
        // entrance falls to the subtree deny — and the deny beats the visibility
        // built-in that would otherwise admit both.
        visible["subtree-denied"].ShouldContain(caveChain);
        visible["subtree-denied"].ShouldNotContain(entranceChain);
        visible["subtree-denied"].ShouldNotContain(areaProt);
        visible["subtree-denied"].ShouldContain(caveAuth);
        // Set deny at the Collection level hides an otherwise authenticated cave, but
        // not its entrance (the set contains only the cave; the entrance inherits
        // readability from the cave's visibility, which no entry touched for it).
        visible["set-denied"].ShouldNotContain(caveAuth);
        visible["set-denied"].ShouldContain(caveProt);
        // Set allow at the Collection level opens a private row that nothing else
        // admits — and only that row.
        visible["set-allowed"].ShouldContain(cavePrivate);
        visible["set-allowed"].ShouldNotContain(caveEntryOnly);
        visible["set-allowed"].ShouldNotContain(caveOwnKindPriv);
        // deny own∧cave: their own cave vanishes (deny beats ownership), their
        // entrance — another kind — stays via the Editors allow.
        visible["self-denied"].ShouldNotContain(caveDenyOwn);
        visible["self-denied"].ShouldContain(entranceDenyOwn);
        visible["self-denied"].ShouldContain(caveAuth);
        // The unnarrowed deny-own: their own private cave is gone even though ownership
        // would admit it, while everything else the Editors allow reaches stays.
        visible["own-denied"].ShouldNotContain(caveOwnDeniedPriv);
        visible["own-denied"].ShouldContain(caveAuth);
        visible["own-denied"].ShouldContain(cavePrivate);
        // allow own∧cave: their own private cave is admitted, and the conjunction never
        // widens — somebody else's private cave of the same kind stays hidden in all
        // three forms (a flattening bug that paired "all" with the kind would show it).
        visible["own-kind-allowed"].ShouldContain(caveOwnKindPriv);
        visible["own-kind-allowed"].ShouldNotContain(cavePrivate);
        visible["own-kind-allowed"].ShouldNotContain(caveOwnDeniedPriv);
        visible["own-kind-allowed"].ShouldNotContain(caveEntryOnly);
        visible["own-kind-allowed"].ShouldContain(caveAuth);
    }

    [Fact]
    public async Task Exact_view_rule_agrees_between_domain_and_sql_for_every_caller()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var protection = scope.ServiceProvider.GetRequiredService<FeatureProtection>();
        var connection = db.Database.GetDbConnection();
        var exact = new Dictionary<string, List<Guid>>();

        foreach (var (name, ctx) in await CallersAsync(db))
        {
            var domainIds = (await protection.ExactViewIdsAsync(ctx, candidateIds))
                .OrderBy(id => id).ToList();

            var (fragment, parameters) = AccessSql.ExactViewFragment(ctx, "f");
            parameters.Add("candidate_ids", AccessSql.UuidArray(candidateIds));
            var sqlIds = (await connection.QueryAsync<Guid>(
                    $"SELECT f.id FROM features f WHERE f.deleted_at IS NULL" +
                    $" AND f.id = ANY(@candidate_ids) AND {fragment}",
                    parameters))
                .OrderBy(id => id).ToList();

            sqlIds.ShouldBe(domainIds, $"exact-view parity broke for caller '{name}'");
            exact[name] = domainIds;
        }

        // Anchors: the row owner always sees exactly; club members hold VEL on
        // group-bound roots through the seeded starter ruleset (editable content now,
        // not a hardcoded arm); a grant opens exactly the granted root's subtree — and
        // a chain under TWO protected roots stays closed until every root is granted;
        // strangers get nothing protected.
        exact["owner"].ShouldBe(candidateIds.OrderBy(id => id).ToList());
        exact["groupMate"].ShouldContain(caveCavingGroupProt);
        exact["groupMate"].ShouldContain(entranceCavingGroupProt);
        exact["groupMate"].ShouldNotContain(caveChain);
        exact["groupMate"].ShouldNotContain(entranceChain);
        exact["groupMate"].ShouldNotContain(entranceProt);
        exact["stranger"].ShouldNotContain(areaProt);
        exact["stranger"].ShouldNotContain(caveChain);
        exact["stranger"].ShouldNotContain(entranceChain);
        exact["stranger"].ShouldNotContain(caveProt);
        exact["stranger"].ShouldNotContain(entranceProt);
        exact["stranger"].ShouldNotContain(caveCavingGroupProt);
        exact["stranger"].ShouldNotContain(entranceCavingGroupProt);
        exact["entry-reader"].ShouldNotContain(caveProt);
        exact["entry-reader"].ShouldNotContain(entranceProt);
        exact["vel-grantee"].ShouldContain(areaProt);
        exact["vel-grantee"].ShouldContain(caveProt);
        exact["vel-grantee"].ShouldContain(entranceProt);
        // The grant on the outer area root alone never opens the inner protected cave.
        exact["vel-grantee"].ShouldNotContain(caveChain);
        exact["vel-grantee"].ShouldNotContain(entranceChain);
        // A VEL deny on ONE of two protected roots vetoes the whole chain under it,
        // while the globally allowed VEL still opens the single-root cave.
        exact["vel-denied"].ShouldContain(caveProt);
        exact["vel-denied"].ShouldContain(entranceProt);
        exact["vel-denied"].ShouldNotContain(areaProt);
        exact["vel-denied"].ShouldNotContain(caveChain);
        exact["vel-denied"].ShouldNotContain(entranceChain);
    }

    // ---- seeding helpers ----

    private static AccessEntry Direct(
        Guid userId,
        AccessEffect effect,
        AccessAction actions,
        AccessScopeKind scope,
        Guid? scopeFeatureId = null,
        Guid? scopeId = null,
        FeatureKind? kind = null) => new()
    {
        SubjectKind = AccessSubjectKind.User,
        SubjectId = userId,
        Effect = effect,
        Domain = AccessDomain.Features,
        Actions = actions,
        ScopeKind = scope,
        ScopeFeatureId = scopeFeatureId,
        ScopeId = scopeId,
        FeatureKind = kind,
    };

    private async Task<Guid> CreateCavingGroupAsync()
    {
        var response = await manager.PostAsJsonAsync("/api/v1/caving-groups/", new
        {
            name = $"Parity CavingGroup {Guid.NewGuid():N}"[..30],
            type = "cavingClub",
            description = (string?)null,
            website = (string?)null,
        });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
    }

    private async Task AddMemberAsync(Guid groupId, Guid userId)
    {
        (await manager.PostAsJsonAsync($"/api/v1/caving-groups/{groupId}/members", new
        {
            caverId = await RosterHelper.CaverIdForAsync(factory, userId),
            role = "member",
        })).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateCaveAsync(
        HttpClient author,
        string name,
        string visibility,
        bool locationProtected = false,
        Guid? parentId = null,
        Guid? cavingGroupId = null)
    {
        var response = await author.PostAsJsonAsync("/api/v1/caves", new
        {
            name = $"{name} {Guid.NewGuid():N}"[..40],
            caveTypeId,
            visibility,
            locationProtected,
            parentId,
            cavingGroupId,
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

    private async Task<Guid> AddEntranceAsync(HttpClient author, Guid caveId)
    {
        var response = await author.PostAsJsonAsync($"/api/v1/caves/{caveId}/entrances", new
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

    private async Task ReplaceAccessRulesAsync(Guid featureId, (Guid UserId, AccessAction Actions)[] entries)
    {
        var response = await owner.PutAsJsonAsync($"/api/v1/objects/feature/{featureId}/access", new
        {
            entries = entries.Select(e => new
            {
                subjectKind = "user",
                subjectId = e.UserId,
                effect = "allow",
                actions = e.Actions.ToString().Replace(" ", string.Empty),
                scopeKind = "object",
            }).ToArray(),
        });
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    /// <summary>The caller archetypes, resolved exactly as requests resolve them.</summary>
    private async Task<(string Name, AccessContext Ctx)[]> CallersAsync(SilexGisDbContext db)
    {
        Task<AccessContext> ContextOf(Guid userId) => RosterHelper.AccessContextOfAsync(db, userId);

        return
        [
            ("owner", await ContextOf(ownerId)),
            ("groupMate", await ContextOf(groupMateId)),
            ("stranger", await ContextOf(strangerId)),
            ("entry-reader", await ContextOf(entryReaderId)),
            ("vel-grantee", await ContextOf(velGranteeId)),
            ("subtree-denied", await ContextOf(subtreeDeniedId)),
            ("set-denied", await ContextOf(setDeniedId)),
            ("set-allowed", await ContextOf(setAllowedId)),
            ("self-denied", await ContextOf(selfDeniedId)),
            ("own-denied", await ContextOf(ownDeniedId)),
            ("own-kind-allowed", await ContextOf(ownKindAllowedId)),
            ("vel-denied", await ContextOf(velDeniedId)),
        ];
    }

    /// <summary>The evaluated facts of every candidate row, straight from storage — the
    /// pure evaluator's leg of the parity.</summary>
    private static async Task<Dictionary<Guid, AccessTargetFacts>> FactsForAsync(
        SilexGisDbContext db, Guid[] ids)
    {
        var rows = await db.Features.AsNoTracking()
            .Where(f => ids.Contains(f.Id))
            .Select(f => new { f.Id, f.OwnerUserId, f.CavingGroupId, f.Kind, f.FeatureTypeId, f.AncestorIds })
            .ToListAsync();
        var chainIds = rows.SelectMany(r => r.AncestorIds).Distinct().ToArray();
        var trios = await db.Features.AsNoTracking()
            .Where(f => chainIds.Contains(f.Id))
            .Select(f => new { f.Id, f.Visibility, f.CavingGroupId })
            .ToDictionaryAsync(x => x.Id);
        var sets = (await db.FeatureSetMembers.AsNoTracking()
                .Where(m => ids.Contains(m.FeatureId)).ToListAsync())
            .ToLookup(m => m.FeatureId, m => m.FeatureSetId);

        return rows.ToDictionary(r => r.Id, r => new AccessTargetFacts
        {
            ObjectId = r.Id,
            OwnerUserId = r.OwnerUserId,
            CavingGroupId = r.CavingGroupId,
            AncestorIds = r.AncestorIds,
            FeatureKind = r.Kind,
            FeatureTypeId = r.FeatureTypeId,
            FeatureSetIds = [.. sets[r.Id]],
            VisibilityChain =
            [
                .. r.AncestorIds.Where(trios.ContainsKey)
                    .Select(a => new VisibilityFact(trios[a].Visibility, trios[a].CavingGroupId)),
            ],
        });
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
