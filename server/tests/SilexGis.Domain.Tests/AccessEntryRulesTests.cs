// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The scope-validity table (rejected, never silently ignored) and the
/// no-amplification bound on entry authoring.
/// </summary>
public class AccessEntryRulesTests
{
    private static readonly Guid Anchor = Guid.CreateVersion7();
    private static readonly Guid CallerId = Guid.CreateVersion7();

    private static AccessEntrySnapshot Entry(
        AccessDomain domain,
        AccessAction actions,
        AccessScopeKind scope,
        Guid? scopeFeatureId = null,
        Guid? scopeId = null,
        FeatureKind? kind = null,
        long? typeId = null,
        AccessEffect effect = AccessEffect.Allow) => new(
        1, PermissionGroupId: Guid.CreateVersion7(), SubjectKind: null, SubjectId: null,
        effect, domain, actions, scope, scopeFeatureId, scopeId, kind, typeId);

    [Fact]
    public void Well_formed_entries_pass()
    {
        AccessEntryRules.Validate(Entry(AccessDomain.Features, AccessAction.Read, AccessScopeKind.All))
            .ShouldBeNull();
        AccessEntryRules.Validate(Entry(AccessDomain.TripLogs, AccessAction.Write, AccessScopeKind.Own))
            .ShouldBeNull();
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Create, AccessScopeKind.CavingGroup, scopeId: Anchor))
            .ShouldBeNull();
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Create | AccessAction.Write, AccessScopeKind.Subtree,
            scopeFeatureId: Anchor)).ShouldBeNull();
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Read, AccessScopeKind.FeatureSet, scopeId: Anchor))
            .ShouldBeNull();
        AccessEntryRules.Validate(Entry(
            AccessDomain.CavingGroups, AccessAction.Write | AccessAction.ManagePermissions,
            AccessScopeKind.Object, scopeId: Anchor)).ShouldBeNull();
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Read, AccessScopeKind.Own, kind: FeatureKind.Cave))
            .ShouldBeNull();
    }

    [Fact]
    public void Own_and_caving_group_scopes_exist_only_in_trio_domains()
    {
        AccessEntryRules.Validate(Entry(AccessDomain.Tags, AccessAction.Read, AccessScopeKind.Own))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Users, AccessAction.Read, AccessScopeKind.CavingGroup, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    [Fact]
    public void Subtree_and_set_scopes_exist_only_in_the_feature_domain()
    {
        AccessEntryRules.Validate(Entry(
            AccessDomain.TripLogs, AccessAction.Read, AccessScopeKind.Subtree, scopeFeatureId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Geofiles, AccessAction.Read, AccessScopeKind.FeatureSet, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    [Fact]
    public void Create_is_rejected_where_it_is_meaningless()
    {
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Create, AccessScopeKind.Own))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Create, AccessScopeKind.FeatureSet, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Create, AccessScopeKind.Object, scopeFeatureId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    [Fact]
    public void Documents_express_the_owned_content_scopes_and_no_others()
    {
        // A document row carries the owner/caving-group/visibility trio, so every scope
        // that keys on one of those columns has a faithful flat form here.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessActions.Everything, AccessScopeKind.All)).ShouldBeNull();
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read | AccessAction.Write, AccessScopeKind.Own))
            .ShouldBeNull();
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read, AccessScopeKind.CavingGroup, scopeId: Anchor))
            .ShouldBeNull();
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read | AccessAction.Share, AccessScopeKind.Object,
            scopeId: Anchor)).ShouldBeNull();

        // The one collection a document belongs to is the cabinet it is filed in, and the
        // rule covers everything filed below that cabinet as well.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read | AccessAction.Write, AccessScopeKind.Cabinet,
            scopeId: Anchor)).ShouldBeNull();

        // Filing an existing document is a write on that document, not a creation into
        // the cabinet — so there is nothing for Create to key on here.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Create, AccessScopeKind.Cabinet, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);

        // A cabinet anchors on the plain scope id; the feature anchor column is not a
        // second home for it, and an unanchored cabinet rule names no cabinet at all.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read, AccessScopeKind.Cabinet, scopeFeatureId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read, AccessScopeKind.Cabinet))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);

        // Cabinets file documents and nothing else, so the scope means nothing elsewhere.
        AccessEntryRules.Validate(Entry(
            AccessDomain.TripLogs, AccessAction.Read, AccessScopeKind.Cabinet, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);

        // A document is not a feature: it sits in no containment DAG and joins no named
        // feature set, so those two collection scopes still have no honest answer here.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read, AccessScopeKind.Subtree, scopeFeatureId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read, AccessScopeKind.FeatureSet, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);

        // Kind and type narrowing describe features; there is nothing to narrow here.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read, AccessScopeKind.All, kind: FeatureKind.Cave))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);

        // Outside the feature domain the object anchor is scope_id; the feature anchor
        // column is not a second home for it.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Read, AccessScopeKind.Object, scopeFeatureId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    [Fact]
    public void Uploading_a_document_is_a_create_right_and_lands_only_where_it_can_be_keyed()
    {
        // "Who may upload a document" is Create in the documents domain. It is
        // expressible domain-wide and against a club binding — the two shapes with a
        // prospective row to evaluate — and refused where there is none.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Create, AccessScopeKind.All)).ShouldBeNull();
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Create | AccessAction.Read,
            AccessScopeKind.CavingGroup, scopeId: Anchor)).ShouldBeNull();

        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Create, AccessScopeKind.Own))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Documents, AccessAction.Create, AccessScopeKind.Object, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    [Fact]
    public void Object_scope_is_rejected_in_domains_without_per_object_identity()
    {
        AccessEntryRules.Validate(Entry(
            AccessDomain.Settings, AccessAction.Read, AccessScopeKind.Object, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Audit, AccessAction.Read, AccessScopeKind.Object, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    [Fact]
    public void Narrowing_is_rejected_outside_all_and_own_in_the_feature_domain()
    {
        // No faithful flat evaluation exists at subtree/set/object scopes — "deny
        // entrances under area X" is a feature set, never a narrowed subtree.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Read, AccessScopeKind.Subtree,
            scopeFeatureId: Anchor, kind: FeatureKind.CaveEntrance))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.TripLogs, AccessAction.Read, AccessScopeKind.All, kind: FeatureKind.Cave))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    [Fact]
    public void Narrowing_by_kind_and_type_together_is_rejected()
    {
        // The flat arrays would evaluate the pair as a disjunction and silently widen.
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Read, AccessScopeKind.All,
            kind: FeatureKind.Generic, typeId: 42))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    [Fact]
    public void Anchor_columns_must_match_the_scope()
    {
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Read, AccessScopeKind.All, scopeFeatureId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Read, AccessScopeKind.Subtree))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.Features, AccessAction.Read, AccessScopeKind.Object, scopeId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
        AccessEntryRules.Validate(Entry(
            AccessDomain.TripLogs, AccessAction.Read, AccessScopeKind.Object, scopeFeatureId: Anchor))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    [Fact]
    public void Empty_actions_and_double_homes_are_rejected()
    {
        AccessEntryRules.Validate(Entry(AccessDomain.Features, AccessAction.None, AccessScopeKind.All))
            .ShouldBe(AccessEntryRules.ScopeInvalidCode);

        var bothHomes = new AccessEntrySnapshot(
            1, Guid.CreateVersion7(), AccessSubjectKind.User, CallerId,
            AccessEffect.Allow, AccessDomain.Features, AccessAction.Read, AccessScopeKind.All,
            null, null, null, null);
        AccessEntryRules.Validate(bothHomes).ShouldBe(AccessEntryRules.ScopeInvalidCode);

        var noHome = new AccessEntrySnapshot(
            1, null, null, null,
            AccessEffect.Allow, AccessDomain.Features, AccessAction.Read, AccessScopeKind.All,
            null, null, null, null);
        AccessEntryRules.Validate(noHome).ShouldBe(AccessEntryRules.ScopeInvalidCode);
    }

    // ---------- no amplification ----------

    [Fact]
    public void Authoring_is_bounded_by_what_the_author_holds()
    {
        var anchor = new AccessTargetFacts { ObjectId = Anchor, OwnerUserId = Guid.CreateVersion7() };
        var author = new AccessContext(CallerId, false, [],
        [
            new AccessEntrySnapshot(
                1, Guid.CreateVersion7(), null, null, AccessEffect.Allow, AccessDomain.Features,
                AccessAction.Read | AccessAction.Write | AccessAction.ManagePermissions,
                AccessScopeKind.Object, Anchor, null, null, null),
        ]);

        var proposal = Entry(
            AccessDomain.Features,
            AccessAction.Read | AccessAction.Write | AccessAction.ViewExactLocation,
            AccessScopeKind.Object, scopeFeatureId: Anchor);

        // Granting exact view requires holding exact view on the target.
        AccessEntryRules.ExceededActions(author, proposal, anchor)
            .ShouldBe(AccessAction.ViewExactLocation);
    }

    [Fact]
    public void Owners_may_grant_anything_on_their_own_object_and_full_admins_everywhere()
    {
        var owned = new AccessTargetFacts { ObjectId = Anchor, OwnerUserId = CallerId };
        var proposal = Entry(AccessDomain.Features, AccessActions.Everything, AccessScopeKind.Object,
            scopeFeatureId: Anchor);

        // The ownership built-in answers every action on the anchor.
        AccessEntryRules.ExceededActions(new AccessContext(CallerId, false, [], []), proposal, owned)
            .ShouldBe(AccessAction.None);

        AccessEntryRules.ExceededActions(new AccessContext(CallerId, true, [], []), proposal,
                new AccessTargetFacts { ObjectId = Anchor, OwnerUserId = Guid.CreateVersion7() })
            .ShouldBe(AccessAction.None);
    }

    [Fact]
    public void A_delegated_editor_cannot_insert_rights_they_lack()
    {
        // The classic case: an editor of one permission group proposing
        // "allow · Users · all" without holding that right themselves.
        var author = new AccessContext(CallerId, false, [], []);
        var proposal = Entry(AccessDomain.Users, AccessAction.Read | AccessAction.Write, AccessScopeKind.All);

        AccessEntryRules.ExceededActions(author, proposal, null)
            .ShouldBe(AccessAction.Read | AccessAction.Write);
    }
}
