// SPDX-License-Identifier: AGPL-3.0-or-later
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
/// Parity of the THREE synchronized forms of the access rule over documents: the pure
/// evaluator (<c>AccessEvaluator.Decide</c>), the EF filter (<c>Documents.VisibleTo</c>)
/// and the Dapper fragment (<c>AccessSql.VisibleToFragment</c>). A document carries the
/// owner/caving-group/visibility trio and contains nothing, so it expresses four scopes —
/// domain-wide, own, caving-group, one object — over two precedence levels, and this
/// suite drives every one of them in both effects, for thirteen caller archetypes, over
/// one seeded matrix. Each caller's set is asserted to be the same set in all three forms
/// AND to be the right set: three forms agreeing on nonsense would still be parity.
/// A mismatch here is a disclosure, not a flake.
///
/// Every archetype is a Viewer — a Viewer holds nothing over documents beyond the
/// built-ins, so a caller that cannot read a row genuinely has no grant reaching it
/// rather than one that happens not to fire. The one exception is the administrator,
/// whose whole point is that the walk never runs for them.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentAccessParityTests : IAsyncLifetime, IDisposable
{
    private readonly SilexGisApiFactory factory;

    private Guid ownerId;          // owns most of the matrix; no document entries at all
    private Guid strangerId;       // nothing: no entries, no club — the built-ins alone
    private Guid groupMateId;      // in the club, no entries
    private Guid objectAllowedId;  // one object-scope allow
    private Guid objectDeniedId;   // domain-wide allow, one object-scope deny
    private Guid objectOverAllId;  // domain-wide deny, one object-scope allow
    private Guid groupScopedId;    // caving-group-scope allow, NOT a member of the club
    private Guid groupDeniedId;    // domain-wide allow, caving-group-scope deny
    private Guid ownAllowedId;     // own-scope allow; owns one private document
    private Guid ownDeniedId;      // own-scope deny; owns one openly-visible document
    private Guid allAllowedId;     // domain-wide allow
    private Guid allDeniedId;      // domain-wide deny
    private Guid administratorId;  // Full Administrators

    private Guid cavingGroupId;
    private Guid docPrivate;        // owner, private, unbound — nothing admits it
    private Guid docAuthenticated;  // owner, authenticated — the read audience built-in
    private Guid docPublic;         // owner, public
    private Guid docGroupVisible;   // owner, caving-group visibility, bound to the club
    private Guid docObjectGranted;  // owner, private — the object-scope allow target
    private Guid docObjectDenied;   // owner, authenticated — the object-scope deny target
    private Guid docGroupBound;     // owner, private, bound to the club
    private Guid docOwnAllowed;     // owned by ownAllowed, private
    private Guid docOwnDenied;      // owned by ownDenied, authenticated
    private Guid[] candidateIds = [];

    public DocumentAccessParityTests(PostgresFixture postgres) =>
        factory = new SilexGisApiFactory(postgres.ConnectionString);

    public async Task InitializeAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];

        Task<Guid> ViewerAsync(string tag) =>
            AuthHelper.CreateUserAsync(factory, GlobalRoles.Viewer, $"docpar-{tag}-{suffix}@t.local");

        ownerId = await ViewerAsync("own");
        strangerId = await ViewerAsync("str");
        groupMateId = await ViewerAsync("mate");
        objectAllowedId = await ViewerAsync("obja");
        objectDeniedId = await ViewerAsync("objd");
        objectOverAllId = await ViewerAsync("objo");
        groupScopedId = await ViewerAsync("cga");
        groupDeniedId = await ViewerAsync("cgd");
        ownAllowedId = await ViewerAsync("owna");
        ownDeniedId = await ViewerAsync("ownd");
        allAllowedId = await ViewerAsync("alla");
        allDeniedId = await ViewerAsync("alld");
        administratorId = await AuthHelper.CreateUserAsync(
            factory, GlobalRoles.Admin, $"docpar-adm-{suffix}@t.local");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();

        // The club is a row fact of the documents bound to it; membership is what makes
        // caving-group VISIBILITY apply, and the two are deliberately different questions.
        var group = new CavingGroup { Name = $"Doc Parity {suffix}", Slug = $"doc-parity-{suffix}" };
        cavingGroupId = group.Id;
        db.CavingGroups.Add(group);
        await db.SaveChangesAsync();
        await RosterHelper.AddMemberAsync(db, cavingGroupId, groupMateId);
        await RosterHelper.AddMemberAsync(db, cavingGroupId, groupDeniedId);

        docPrivate = Seed(db, "Private", ownerId, Visibility.Private);
        docAuthenticated = Seed(db, "Authenticated", ownerId, Visibility.Authenticated);
        docPublic = Seed(db, "Public", ownerId, Visibility.Public);
        docGroupVisible = Seed(db, "Club visible", ownerId, Visibility.CavingGroup, cavingGroupId);
        docObjectGranted = Seed(db, "Object granted", ownerId, Visibility.Private);
        docObjectDenied = Seed(db, "Object denied", ownerId, Visibility.Authenticated);
        docGroupBound = Seed(db, "Club bound", ownerId, Visibility.Private, cavingGroupId);
        docOwnAllowed = Seed(db, "Own allowed", ownAllowedId, Visibility.Private);
        docOwnDenied = Seed(db, "Own denied", ownDeniedId, Visibility.Authenticated);

        // Every rule is seeded straight into storage: this suite is about the three forms
        // of the evaluation, not about the surface that authors the rules.
        db.AccessEntries.AddRange(
            Direct(objectAllowedId, AccessEffect.Allow, AccessScopeKind.Object, docObjectGranted),

            Direct(objectDeniedId, AccessEffect.Allow, AccessScopeKind.All),
            Direct(objectDeniedId, AccessEffect.Deny, AccessScopeKind.Object, docObjectDenied),

            Direct(objectOverAllId, AccessEffect.Deny, AccessScopeKind.All),
            Direct(objectOverAllId, AccessEffect.Allow, AccessScopeKind.Object, docObjectGranted),

            Direct(groupScopedId, AccessEffect.Allow, AccessScopeKind.CavingGroup, cavingGroupId),

            Direct(groupDeniedId, AccessEffect.Allow, AccessScopeKind.All),
            Direct(groupDeniedId, AccessEffect.Deny, AccessScopeKind.CavingGroup, cavingGroupId),

            Direct(ownAllowedId, AccessEffect.Allow, AccessScopeKind.Own),
            Direct(ownDeniedId, AccessEffect.Deny, AccessScopeKind.Own),
            Direct(allAllowedId, AccessEffect.Allow, AccessScopeKind.All),
            Direct(allDeniedId, AccessEffect.Deny, AccessScopeKind.All));

        await db.SaveChangesAsync();

        candidateIds =
        [
            docPrivate, docAuthenticated, docPublic, docGroupVisible, docObjectGranted,
            docObjectDenied, docGroupBound, docOwnAllowed, docOwnDenied,
        ];
    }

    [Fact]
    public async Task Read_filter_agrees_between_evaluator_ef_and_sql_for_every_caller()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var connection = db.Database.GetDbConnection();
        var facts = await FactsForAsync(db);
        var visible = new Dictionary<string, List<Guid>>();

        foreach (var (name, ctx) in await CallersAsync(db))
        {
            var efIds = await db.Documents.AsNoTracking().VisibleTo(ctx, AccessDomain.Documents)
                .Where(d => candidateIds.Contains(d.Id))
                .Select(d => d.Id).OrderBy(id => id).ToListAsync();

            var (fragment, parameters) = AccessSql.VisibleToFragment(ctx, AccessDomain.Documents, "d");
            parameters.Add("candidate_ids", AccessSql.UuidArray(candidateIds));
            var sqlIds = (await connection.QueryAsync<Guid>(
                    $"SELECT d.id FROM documents d WHERE d.id = ANY(@candidate_ids) AND {fragment}",
                    parameters))
                .OrderBy(id => id).ToList();

            var evaluatorIds = candidateIds
                .Where(id => AccessEvaluator.Decide(
                    ctx, AccessDomain.Documents, AccessAction.Read, facts[id]).Allowed)
                .OrderBy(id => id).ToList();

            sqlIds.ShouldBe(efIds, $"EF↔SQL visibility parity broke for caller '{name}'");
            evaluatorIds.ShouldBe(efIds, $"evaluator↔EF visibility parity broke for caller '{name}'");
            visible[name] = efIds;
        }

        var everything = candidateIds.OrderBy(id => id).ToList();

        // Nothing but the built-ins. The uploader reaches their own documents whatever
        // their audience; somebody else's private document stays shut.
        visible["owner"].ShouldContain(docPrivate);
        visible["owner"].ShouldContain(docGroupBound);
        visible["owner"].ShouldContain(docOwnDenied);       // authenticated, so its audience admits them
        visible["owner"].ShouldNotContain(docOwnAllowed);   // private and not theirs

        // The read audience, and only the read audience: a caller with no club and no
        // rule sees what is published and nothing else.
        visible["stranger"].ShouldContain(docAuthenticated);
        visible["stranger"].ShouldContain(docPublic);
        visible["stranger"].ShouldNotContain(docPrivate);
        visible["stranger"].ShouldNotContain(docGroupVisible);
        visible["stranger"].ShouldNotContain(docGroupBound);
        visible["stranger"].ShouldNotContain(docObjectGranted);

        // Club membership opens caving-group VISIBILITY and nothing more — being in the
        // club that a private document is filed under is not itself a grant.
        visible["group-mate"].ShouldContain(docGroupVisible);
        visible["group-mate"].ShouldContain(docPublic);
        visible["group-mate"].ShouldNotContain(docGroupBound);
        visible["group-mate"].ShouldNotContain(docPrivate);

        // Object level, allow: exactly the named document, nothing beside it.
        visible["object-allowed"].ShouldContain(docObjectGranted);
        visible["object-allowed"].ShouldContain(docPublic);
        visible["object-allowed"].ShouldNotContain(docPrivate);
        visible["object-allowed"].ShouldNotContain(docGroupBound);

        // Object level, deny, over a domain-wide allow: one hole in an otherwise
        // complete view — and the deny beats the audience that would have admitted it.
        visible["object-denied"].ShouldBe(everything.Where(id => id != docObjectDenied).ToList());

        // Object level, allow, over a domain-wide deny: the more specific level decides,
        // so the single named document survives a rule that closes everything.
        visible["object-over-all"].ShouldBe([docObjectGranted]);

        // The caving-group scope keys on the document's binding, not on the caller's
        // membership: this caller is in no club and still reads both bound documents,
        // including the private one no audience would have opened.
        visible["group-scoped"].ShouldContain(docGroupBound);
        visible["group-scoped"].ShouldContain(docGroupVisible);
        visible["group-scoped"].ShouldNotContain(docPrivate);
        visible["group-scoped"].ShouldNotContain(docObjectGranted);

        // Same level, deny wins: a club-scoped deny carves the club's documents out of a
        // domain-wide allow and leaves the rest of it standing.
        visible["group-denied"].ShouldBe(
            everything.Where(id => id != docGroupBound && id != docGroupVisible).ToList());

        // Own scope reaches the caller's own rows and stops: flattened as a domain-wide
        // allow it would have opened every other private document in the matrix.
        visible["own-allowed"].ShouldContain(docOwnAllowed);
        visible["own-allowed"].ShouldContain(docPublic);
        visible["own-allowed"].ShouldNotContain(docPrivate);
        visible["own-allowed"].ShouldNotContain(docGroupBound);

        // A deny on own documents outranks both built-ins: the caller loses their own
        // document even though ownership and its audience each would have admitted it,
        // while everything they never owned is untouched.
        visible["own-denied"].ShouldNotContain(docOwnDenied);
        visible["own-denied"].ShouldContain(docAuthenticated);
        visible["own-denied"].ShouldContain(docPublic);

        // The domain-wide pair, in both directions. The deny takes the public document
        // too — an entry, once it matches, is the whole answer.
        visible["all-allowed"].ShouldBe(everything);
        visible["all-denied"].ShouldBeEmpty();

        // Full Administrators are decided before any entry is read, which is what makes
        // the group the recovery path out of a deny.
        visible["administrator"].ShouldBe(everything);
    }

    [Fact]
    public async Task Point_checks_agree_with_the_list_filter_for_every_caller_and_document()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SilexGisDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAccessService>();
        var documents = await db.Documents.AsNoTracking()
            .Where(d => candidateIds.Contains(d.Id)).ToListAsync();

        foreach (var (name, ctx) in await CallersAsync(db))
        {
            var filtered = (await db.Documents.AsNoTracking().VisibleTo(ctx, AccessDomain.Documents)
                .Where(d => candidateIds.Contains(d.Id))
                .Select(d => d.Id).ToListAsync()).ToHashSet();

            foreach (var document in documents)
            {
                // The service resolves the domain from the row's type and its facts from
                // storage: a document that answers one way when asked about directly and
                // another way when listed is the same bug as a form mismatch.
                var decided = await access.DecideAsync(ctx, AccessAction.Read, document);
                decided.Allowed.ShouldBe(
                    filtered.Contains(document.Id),
                    $"point check and list filter disagree for caller '{name}' on '{document.Title}'");
            }
        }
    }

    // ---- seeding helpers ----

    private static Guid Seed(
        SilexGisDbContext db,
        string title,
        Guid ownerUserId,
        Visibility visibility,
        Guid? cavingGroupId = null)
    {
        var document = new Document
        {
            Title = title,
            OwnerUserId = ownerUserId,
            Visibility = visibility,
            CavingGroupId = cavingGroupId,
        };
        db.Documents.Add(document);
        return document.Id;
    }

    /// <summary>
    /// One direct rule over documents. Outside the feature domain an object anchor is the
    /// plain scope id — the feature anchor column is not a second home for it, and the
    /// stored CHECK constraint refuses an entry that puts it there.
    /// </summary>
    private static AccessEntry Direct(
        Guid userId,
        AccessEffect effect,
        AccessScopeKind scope,
        Guid? scopeId = null) => new()
        {
            SubjectKind = AccessSubjectKind.User,
            SubjectId = userId,
            Effect = effect,
            Domain = AccessDomain.Documents,
            Actions = AccessAction.Read,
            ScopeKind = scope,
            ScopeId = scopeId,
        };

    /// <summary>The caller archetypes, resolved exactly as requests resolve them.</summary>
    private async Task<(string Name, AccessContext Ctx)[]> CallersAsync(SilexGisDbContext db)
    {
        Task<AccessContext> ContextOf(Guid userId) => RosterHelper.AccessContextOfAsync(db, userId);

        return
        [
            ("owner", await ContextOf(ownerId)),
            ("stranger", await ContextOf(strangerId)),
            ("group-mate", await ContextOf(groupMateId)),
            ("object-allowed", await ContextOf(objectAllowedId)),
            ("object-denied", await ContextOf(objectDeniedId)),
            ("object-over-all", await ContextOf(objectOverAllId)),
            ("group-scoped", await ContextOf(groupScopedId)),
            ("group-denied", await ContextOf(groupDeniedId)),
            ("own-allowed", await ContextOf(ownAllowedId)),
            ("own-denied", await ContextOf(ownDeniedId)),
            ("all-allowed", await ContextOf(allAllowedId)),
            ("all-denied", await ContextOf(allDeniedId)),
            ("administrator", await ContextOf(administratorId)),
        ];
    }

    /// <summary>The evaluated facts of every candidate row, straight from storage — the
    /// pure evaluator's leg of the parity.</summary>
    private async Task<Dictionary<Guid, AccessTargetFacts>> FactsForAsync(SilexGisDbContext db) =>
        (await db.Documents.AsNoTracking().Where(d => candidateIds.Contains(d.Id)).ToListAsync())
            .ToDictionary(d => d.Id, AccessTargetFacts.Of);

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose() => factory.Dispose();
}
