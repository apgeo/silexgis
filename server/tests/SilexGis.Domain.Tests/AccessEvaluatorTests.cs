// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The precedence walk: three specificity levels, deny beats allow within a level, the
/// first matching level decides, built-ins (ownership, read-time visibility over the
/// ancestor chain) only when no entry matched, Full Administrators above everything,
/// default deny beneath everything. These facts are the design — the EF and SQL twins
/// are pinned against this evaluator by the parity suite.
/// </summary>
public class AccessEvaluatorTests
{
    private static readonly Guid CallerId = Guid.CreateVersion7();
    private static readonly Guid OtherId = Guid.CreateVersion7();
    private static readonly Guid GroupId = Guid.CreateVersion7();
    private static readonly Guid RowId = Guid.CreateVersion7();
    private static readonly Guid AreaId = Guid.CreateVersion7();
    private static readonly Guid SetId = Guid.CreateVersion7();

    private static long nextEntryId = 1;

    private static AccessEntrySnapshot Entry(
        AccessEffect effect,
        AccessAction actions,
        AccessScopeKind scope,
        AccessDomain domain = AccessDomain.Features,
        Guid? scopeFeatureId = null,
        Guid? scopeId = null,
        FeatureKind? kind = null,
        long? typeId = null) => new(
        nextEntryId++, PermissionGroupId: Guid.CreateVersion7(), SubjectKind: null, SubjectId: null,
        effect, domain, actions, scope, scopeFeatureId, scopeId, kind, typeId);

    private static AccessContext Context(
        params AccessEntrySnapshot[] entries) => new(CallerId, false, [GroupId], entries);

    private static AccessContext FullAdmin() => new(CallerId, true, [], []);

    /// <summary>A feature row owned by someone else, under the area, in the set.</summary>
    private static AccessTargetFacts Row(
        Guid? owner = null,
        Guid? cavingGroupId = null,
        Visibility visibility = Visibility.Private,
        FeatureKind kind = FeatureKind.Cave,
        long? typeId = null,
        Guid[]? setIds = null,
        VisibilityFact[]? ancestors = null) => new()
    {
        ObjectId = RowId,
        OwnerUserId = owner ?? OtherId,
        CavingGroupId = cavingGroupId,
        AncestorIds = [RowId, AreaId],
        FeatureKind = kind,
        FeatureTypeId = typeId,
        FeatureSetIds = setIds ?? [],
        VisibilityChain =
        [
            new VisibilityFact(visibility, cavingGroupId),
            .. ancestors ?? [],
        ],
    };

    private static AccessDecision Decide(
        AccessContext? ctx, AccessTargetFacts? facts, AccessAction action = AccessAction.Read) =>
        AccessEvaluator.Decide(ctx, AccessDomain.Features, action, facts);

    // ---------- the walk itself ----------

    [Fact]
    public void Anonymous_is_denied_before_any_rule()
    {
        var decision = Decide(null, Row(visibility: Visibility.Public));
        decision.Allowed.ShouldBeFalse();
        decision.Source.ShouldBe(AccessDecisionSource.Anonymous);
    }

    [Fact]
    public void Full_administrators_are_unreachable_by_deny()
    {
        // Even a direct object-level deny cannot reach them: the membership
        // short-circuit decides before any entry is consulted.
        var decision = AccessEvaluator.Decide(
            new AccessContext(CallerId, true, [],
                [Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Object, scopeFeatureId: RowId)]),
            AccessDomain.Features, AccessAction.Read, Row());
        decision.Allowed.ShouldBeTrue();
        decision.Source.ShouldBe(AccessDecisionSource.FullAdministrators);
    }

    [Fact]
    public void Object_allow_beats_subtree_deny()
    {
        // "Deny the whole subtree, but allow this one cave": the object-level allow
        // decides before the collection-level deny is even consulted.
        var ctx = Context(
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Subtree, scopeFeatureId: AreaId),
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, scopeFeatureId: RowId));

        var decision = Decide(ctx, Row());
        decision.Allowed.ShouldBeTrue();
        decision.Level.ShouldBe(AccessLevel.Object);
    }

    [Fact]
    public void Subtree_deny_beats_global_allow()
    {
        var ctx = Context(
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All),
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Subtree, scopeFeatureId: AreaId));

        var decision = Decide(ctx, Row());
        decision.Allowed.ShouldBeFalse();
        decision.Level.ShouldBe(AccessLevel.Collection);
    }

    [Fact]
    public void Feature_set_entries_sit_at_the_collection_level()
    {
        var setAllow = Context(
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.FeatureSet, scopeId: SetId));
        Decide(setAllow, Row(setIds: [SetId])).Allowed.ShouldBeTrue();
        Decide(setAllow, Row(setIds: [])).Allowed.ShouldBeFalse();

        // A set deny at the collection level beats a global allow, and ties with a
        // subtree allow at the same level resolve to deny.
        var ctx = Context(
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All),
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Subtree, scopeFeatureId: AreaId),
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.FeatureSet, scopeId: SetId));
        var decision = Decide(ctx, Row(setIds: [SetId]));
        decision.Allowed.ShouldBeFalse();
        decision.Level.ShouldBe(AccessLevel.Collection);
    }

    [Fact]
    public void Deny_beats_allow_within_a_level()
    {
        var ctx = Context(
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All),
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.CavingGroup, scopeId: GroupId));

        // Both entries live at the Global level; the deny wins there.
        var decision = Decide(ctx, Row(cavingGroupId: GroupId));
        decision.Allowed.ShouldBeFalse();
        decision.Level.ShouldBe(AccessLevel.Global);

        // On a row outside the group only the allow matches.
        Decide(ctx, Row()).Allowed.ShouldBeTrue();
    }

    [Fact]
    public void Deny_beats_ownership_and_visibility()
    {
        var ctx = Context(Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.All));

        // An owner can be locked out of their own, publicly visible object; Full
        // Administrators are the recovery path.
        Decide(ctx, Row(owner: CallerId, visibility: Visibility.Public)).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Membership_path_never_affects_weight()
    {
        // The same rule as a direct entry and as a ruleset entry: identical outcome —
        // only scope specificity and effect matter.
        var direct = new AccessEntrySnapshot(
            1, null, AccessSubjectKind.User, CallerId, AccessEffect.Allow, AccessDomain.Features,
            AccessAction.Read, AccessScopeKind.All, null, null, null, null);
        var viaRuleset = Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All);

        Decide(Context(direct), Row()).Allowed
            .ShouldBe(Decide(Context(viaRuleset), Row()).Allowed);
    }

    // ---------- built-ins ----------

    [Fact]
    public void Ownership_grants_any_action_when_no_entry_matched()
    {
        var decision = Decide(Context(), Row(owner: CallerId), AccessAction.Delete);
        decision.Allowed.ShouldBeTrue();
        decision.Source.ShouldBe(AccessDecisionSource.Ownership);
    }

    [Fact]
    public void Visibility_admits_read_only()
    {
        var facts = Row(visibility: Visibility.Authenticated);
        Decide(Context(), facts, AccessAction.Read).Allowed.ShouldBeTrue();
        Decide(Context(), facts, AccessAction.Write).Allowed.ShouldBeFalse();
        Decide(Context(), facts, AccessAction.Create).Allowed.ShouldBeFalse();
        Decide(Context(), facts, AccessAction.Execute).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Visibility_cascades_over_the_ancestor_chain()
    {
        // A private entrance row under an authenticated cave is readable — the D1d
        // read-time cascade; the same row with a fully private chain is not.
        var inherited = Row(
            visibility: Visibility.Private,
            ancestors: [new VisibilityFact(Visibility.Authenticated, null)]);
        Decide(Context(), inherited).Allowed.ShouldBeTrue();
        Decide(Context(), inherited).Source.ShouldBe(AccessDecisionSource.Visibility);

        Decide(Context(), Row(visibility: Visibility.Private)).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Caving_group_visibility_admits_members_of_that_links_group()
    {
        var mine = Row(
            visibility: Visibility.Private,
            ancestors: [new VisibilityFact(Visibility.CavingGroup, GroupId)]);
        Decide(Context(), mine).Allowed.ShouldBeTrue();

        var foreign = Row(
            visibility: Visibility.Private,
            ancestors: [new VisibilityFact(Visibility.CavingGroup, OtherId)]);
        Decide(Context(), foreign).Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Default_is_deny()
    {
        var decision = Decide(Context(), Row());
        decision.Allowed.ShouldBeFalse();
        decision.Source.ShouldBe(AccessDecisionSource.DefaultDeny);
    }

    // ---------- narrowing conjunctions (the D-v2-7 rule) ----------

    [Fact]
    public void Own_and_kind_never_widens_to_all_and_kind()
    {
        var ctx = Context(Entry(
            AccessEffect.Allow, AccessAction.Write, AccessScopeKind.Own, kind: FeatureKind.Cave));

        // Own row of the right kind: the entry matches. Someone else's row of the right
        // kind: the conjunction fails — the narrowing rides its scope, it is not a
        // second fact that could pair with "all".
        var ownCave = Decide(ctx, Row(owner: CallerId, kind: FeatureKind.Cave), AccessAction.Write);
        ownCave.Allowed.ShouldBeTrue();
        ownCave.Source.ShouldBe(AccessDecisionSource.Entries);
        Decide(ctx, Row(owner: OtherId, kind: FeatureKind.Cave), AccessAction.Write).Allowed.ShouldBeFalse();

        // An own row of another kind is still writable — via the ownership BUILT-IN,
        // not the entry: the narrowed entry contributes nothing there.
        var ownOther = Decide(ctx, Row(owner: CallerId, kind: FeatureKind.CaveEntrance), AccessAction.Write);
        ownOther.Allowed.ShouldBeTrue();
        ownOther.Source.ShouldBe(AccessDecisionSource.Ownership);
    }

    [Fact]
    public void Kind_narrowed_all_matches_rows_of_that_kind_only()
    {
        var ctx = Context(Entry(
            AccessEffect.Deny, AccessAction.Read, AccessScopeKind.All, kind: FeatureKind.CaveEntrance));

        Decide(ctx, Row(kind: FeatureKind.CaveEntrance, visibility: Visibility.Public)).Allowed.ShouldBeFalse();
        Decide(ctx, Row(kind: FeatureKind.Cave, visibility: Visibility.Public)).Allowed.ShouldBeTrue();

        // A narrowed entry speaks about rows: it never matches a target-less check.
        Decide(ctx, null).Source.ShouldBe(AccessDecisionSource.DefaultDeny);
    }

    [Fact]
    public void Type_narrowing_matches_the_data_level_kind()
    {
        var ctx = Context(Entry(
            AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All, typeId: 42));

        Decide(ctx, Row(typeId: 42)).Allowed.ShouldBeTrue();
        Decide(ctx, Row(typeId: 7)).Allowed.ShouldBeFalse();
        Decide(ctx, Row(typeId: null)).Allowed.ShouldBeFalse();
    }

    // ---------- Create against the target context ----------

    [Fact]
    public void Create_evaluates_against_the_parent_and_binding_context()
    {
        var subtree = Context(Entry(
            AccessEffect.Allow, AccessAction.Create, AccessScopeKind.Subtree, scopeFeatureId: AreaId));
        var underArea = AccessTargetFacts.ForCreate(OtherId, [RowId, AreaId], null);
        var elsewhere = AccessTargetFacts.ForCreate(OtherId, [Guid.CreateVersion7()], null);

        Decide(subtree, underArea, AccessAction.Create).Allowed.ShouldBeTrue();
        Decide(subtree, elsewhere, AccessAction.Create).Allowed.ShouldBeFalse();

        var groupScoped = Context(Entry(
            AccessEffect.Allow, AccessAction.Create, AccessScopeKind.CavingGroup, scopeId: GroupId));
        Decide(groupScoped, AccessTargetFacts.ForCreate(null, [], GroupId), AccessAction.Create)
            .Allowed.ShouldBeTrue();
        Decide(groupScoped, AccessTargetFacts.ForCreate(null, [], null), AccessAction.Create)
            .Allowed.ShouldBeFalse();
    }

    [Fact]
    public void Create_under_your_own_feature_rides_the_ownership_built_in()
    {
        var underMine = AccessTargetFacts.ForCreate(CallerId, [AreaId], null);
        var decision = Decide(Context(), underMine, AccessAction.Create);
        decision.Allowed.ShouldBeTrue();
        decision.Source.ShouldBe(AccessDecisionSource.Ownership);

        // Root-level create with no parent and no binding sees Global entries only —
        // and with none, the default deny.
        Decide(Context(), AccessTargetFacts.ForCreate(null, [], null), AccessAction.Create)
            .Allowed.ShouldBeFalse();
    }

    // ---------- explainability ----------

    [Fact]
    public void The_decision_names_its_deciding_entries_and_level()
    {
        var subtreeDeny = Entry(
            AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Subtree, scopeFeatureId: AreaId);
        var globalAllow = Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All);
        var decision = Decide(Context(globalAllow, subtreeDeny), Row());

        decision.Allowed.ShouldBeFalse();
        decision.Source.ShouldBe(AccessDecisionSource.Entries);
        decision.Level.ShouldBe(AccessLevel.Collection);
        decision.DecidingEntries.ShouldHaveSingleItem().EntryId.ShouldBe(subtreeDeny.EntryId);
    }

    // ---------- the flattened form stays faithful ----------

    [Fact]
    public void Flattening_keeps_the_narrowed_conjunctions_apart()
    {
        var entries = new[]
        {
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Own, kind: FeatureKind.Cave),
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.All, typeId: 42),
            Entry(AccessEffect.Allow, AccessAction.Write, AccessScopeKind.CavingGroup, scopeId: GroupId),
        };

        var read = AccessFilterSet.Build(entries, AccessDomain.Features, AccessAction.Read);
        read.AllowOwnKinds.ShouldBe([(short)FeatureKind.Cave]);
        read.AllowAllKinds.ShouldBeEmpty();
        read.AllowOwn.ShouldBeFalse(); // the narrowed entry must NOT set the unnarrowed flag
        read.DenyAllTypeIds.ShouldBe([42L]);
        read.AllowCavingGroupIds.ShouldBeEmpty(); // Write-only entry contributes nothing to Read

        var write = AccessFilterSet.Build(entries, AccessDomain.Features, AccessAction.Write);
        write.AllowCavingGroupIds.ShouldBe([GroupId]);
        write.IsEmpty.ShouldBeFalse();

        AccessFilterSet.Build(entries, AccessDomain.TripLogs, AccessAction.Read).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void Object_scope_anchors_on_the_domains_own_column()
    {
        var featureEntry = Entry(
            AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, scopeFeatureId: RowId);
        var tripEntry = Entry(
            AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object,
            domain: AccessDomain.TripLogs, scopeId: RowId);

        AccessFilterSet.Build([featureEntry], AccessDomain.Features, AccessAction.Read)
            .AllowObjectIds.ShouldBe([RowId]);
        AccessFilterSet.Build([tripEntry], AccessDomain.TripLogs, AccessAction.Read)
            .AllowObjectIds.ShouldBe([RowId]);
    }
}
