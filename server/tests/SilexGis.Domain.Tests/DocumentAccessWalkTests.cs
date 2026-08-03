// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// The access rule over documents, in its authoritative pure form. A document carries the
/// owner/caving-group/visibility trio and contains nothing, so it expresses exactly four
/// scopes — domain-wide, own, caving-group and one object — across two precedence levels:
/// the object level, and the global level everything else lands at. Each of those bands
/// has a faithful flat form in the EF filter and in the SQL fragment; the collection
/// bands (subtree, feature set) have none here, which is why the domain refuses them
/// rather than answering them in one form only. The database-backed parity suite pins all
/// three forms against each other; these tests pin the form the other two are copies of.
/// </summary>
public class DocumentAccessWalkTests
{
    private static readonly Guid CallerId = Guid.CreateVersion7();
    private static readonly Guid OtherId = Guid.CreateVersion7();
    private static readonly Guid GroupId = Guid.CreateVersion7();
    private static readonly Guid OtherGroupId = Guid.CreateVersion7();

    private static long nextEntryId = 1;

    private static AccessEntrySnapshot Entry(
        AccessEffect effect,
        AccessAction actions,
        AccessScopeKind scope,
        Guid? scopeId = null,
        AccessDomain domain = AccessDomain.Documents,
        Guid? scopeFeatureId = null) => new(
        nextEntryId++, PermissionGroupId: Guid.CreateVersion7(), SubjectKind: null, SubjectId: null,
        effect, domain, actions, scope, scopeFeatureId, scopeId, FeatureKind: null, FeatureTypeId: null);

    private static AccessContext Context(params AccessEntrySnapshot[] entries) =>
        new(CallerId, false, [GroupId], entries);

    /// <summary>A document row's facts, taken the way the access service takes them.</summary>
    private static AccessTargetFacts Document(
        Guid? owner = null,
        Visibility visibility = Visibility.Private,
        Guid? cavingGroupId = null,
        Guid? id = null) => AccessTargetFacts.Of(new Document
        {
            Id = id ?? Guid.CreateVersion7(),
            Title = "Survey report",
            OwnerUserId = owner ?? OtherId,
            Visibility = visibility,
            CavingGroupId = cavingGroupId,
        });

    private static bool Allowed(
        AccessContext ctx, AccessTargetFacts facts, AccessAction action = AccessAction.Read) =>
        AccessEvaluator.Decide(ctx, AccessDomain.Documents, action, facts).Allowed;

    [Fact]
    public void One_document_outranks_a_domain_wide_rule_in_both_directions()
    {
        var target = Document(visibility: Visibility.Authenticated);
        var objectId = target.ObjectId!.Value;

        // Level 1 allow over a level 3 deny: the named document is readable and nothing
        // else is.
        var opened = Context(
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.All),
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, scopeId: objectId));
        Allowed(opened, target).ShouldBeTrue();
        Allowed(opened, Document(visibility: Visibility.Public)).ShouldBeFalse();

        // Level 1 deny over a level 3 allow, on a row the visibility built-in would have
        // admitted anyway: the named document is gone and the rest stays.
        var closed = Context(
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All),
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Object, scopeId: objectId));
        Allowed(closed, target).ShouldBeFalse();
        Allowed(closed, Document()).ShouldBeTrue();
    }

    [Fact]
    public void A_caving_group_scope_keys_on_the_documents_binding_not_the_callers_membership()
    {
        // The scope names a club; whether the caller is in it is a different question,
        // and the rule that reaches them has already answered it.
        var ctx = Context(
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.CavingGroup, scopeId: OtherGroupId));

        Allowed(ctx, Document(cavingGroupId: OtherGroupId)).ShouldBeTrue();
        Allowed(ctx, Document(cavingGroupId: GroupId)).ShouldBeFalse();
        Allowed(ctx, Document()).ShouldBeFalse();
    }

    [Fact]
    public void A_caving_group_deny_beats_a_domain_wide_allow_at_the_same_level()
    {
        // Both land at the global level, where any deny decides — this is the shape an
        // installation uses to carve one club's material out of a broad grant.
        var ctx = Context(
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All),
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.CavingGroup, scopeId: GroupId));

        Allowed(ctx, Document(cavingGroupId: GroupId, visibility: Visibility.Public)).ShouldBeFalse();
        Allowed(ctx, Document()).ShouldBeTrue();
    }

    [Fact]
    public void Own_scope_reaches_the_callers_documents_and_stops_there()
    {
        var ctx = Context(Entry(AccessEffect.Allow, AccessAction.Write, AccessScopeKind.Own));

        Allowed(ctx, Document(owner: CallerId), AccessAction.Write).ShouldBeTrue();

        // Flattened as a domain-wide grant instead of a conjunction with ownership, this
        // would hand the caller write access to everybody's documents.
        Allowed(ctx, Document(owner: OtherId), AccessAction.Write).ShouldBeFalse();
    }

    [Fact]
    public void A_deny_on_own_documents_beats_both_built_ins()
    {
        // Ownership and visibility only speak when no entry matched; a deny that matches
        // silences both, and the caller's own openly-visible document goes with them.
        var ctx = Context(Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Own));

        Allowed(ctx, Document(owner: CallerId, visibility: Visibility.Public)).ShouldBeFalse();
        Allowed(ctx, Document(owner: OtherId, visibility: Visibility.Authenticated)).ShouldBeTrue();
    }

    [Fact]
    public void Without_an_entry_a_document_is_reachable_only_by_its_owner_or_its_audience()
    {
        var ctx = Context();

        Allowed(ctx, Document(owner: CallerId)).ShouldBeTrue();
        Allowed(ctx, Document(visibility: Visibility.Authenticated)).ShouldBeTrue();
        Allowed(ctx, Document(visibility: Visibility.CavingGroup, cavingGroupId: GroupId)).ShouldBeTrue();

        Allowed(ctx, Document()).ShouldBeFalse();
        Allowed(ctx, Document(visibility: Visibility.CavingGroup, cavingGroupId: OtherGroupId)).ShouldBeFalse();

        // Visibility is a read audience and nothing more — it never carries a write.
        Allowed(ctx, Document(visibility: Visibility.Public), AccessAction.Write).ShouldBeFalse();
    }

    [Fact]
    public void Reach_through_an_attached_object_admits_a_document_no_other_built_in_reaches()
    {
        // The archive as it stands: a document uploaded onto somebody else's cave is
        // neither owned by nor visible to the people who work on that cave, and reach
        // through the cave is the only thing that has ever opened it.
        var ctx = Context();
        var attached = Document() with { ReachedByAttachment = true };

        Allowed(ctx, attached).ShouldBeTrue();
        Allowed(ctx, attached, AccessAction.Write).ShouldBeTrue();

        // The same document with nothing behind it: private, not theirs, no rule — shut.
        Allowed(ctx, Document()).ShouldBeFalse();
        Allowed(ctx, Document(), AccessAction.Write).ShouldBeFalse();
    }

    [Fact]
    public void An_entry_settles_a_document_before_reach_is_consulted_in_both_directions()
    {
        var target = Document() with { ReachedByAttachment = true };
        var objectId = target.ObjectId!.Value;

        // A deny naming the document is final even for a caller who could read every cave
        // it hangs on. Reach that could talk past a deny would make the deny advisory.
        Allowed(Context(Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.All)), target)
            .ShouldBeFalse();
        Allowed(
            Context(Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Object, scopeId: objectId)),
            target).ShouldBeFalse();

        // And an allow does not need reach: the same caller reads the document whether or
        // not anything is attached to it, which is what makes standalone documents work.
        var granted = Context(Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All));
        Allowed(granted, target).ShouldBeTrue();
        Allowed(granted, Document()).ShouldBeTrue();
    }

    [Fact]
    public void Reach_is_the_weakest_reason_and_never_turns_an_allow_into_a_refusal()
    {
        var ctx = Context();

        // Ownership and the read audience answer without it, so a caller they admit is
        // never made to pay for resolving reach…
        var owned = AccessEvaluator.Decide(
            ctx, AccessDomain.Documents, AccessAction.Read, Document(owner: CallerId));
        owned.Source.ShouldBe(AccessDecisionSource.Ownership);
        AccessEvaluator.AttachmentReachCouldDecide(owned).ShouldBeFalse();

        var published = AccessEvaluator.Decide(
            ctx, AccessDomain.Documents, AccessAction.Read, Document(visibility: Visibility.Public));
        published.Source.ShouldBe(AccessDecisionSource.Visibility);
        AccessEvaluator.AttachmentReachCouldDecide(published).ShouldBeFalse();

        // …nor is a caller an entry already refused, in either effect.
        var denied = AccessEvaluator.Decide(
            Context(Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.All)),
            AccessDomain.Documents,
            AccessAction.Read,
            Document(visibility: Visibility.Public));
        denied.Source.ShouldBe(AccessDecisionSource.Entries);
        AccessEvaluator.AttachmentReachCouldDecide(denied).ShouldBeFalse();

        // Only a question nothing answered is worth the walk over attached objects — and
        // when the walk comes back false the answer is the refusal it already was.
        var open = AccessEvaluator.Decide(ctx, AccessDomain.Documents, AccessAction.Read, Document());
        open.Source.ShouldBe(AccessDecisionSource.DefaultDeny);
        AccessEvaluator.AttachmentReachCouldDecide(open).ShouldBeTrue();

        AccessEvaluator.Decide(
                ctx, AccessDomain.Documents, AccessAction.Read, Document() with { ReachedByAttachment = true })
            .Source.ShouldBe(AccessDecisionSource.Attachment);
    }

    [Fact]
    public void Every_scope_the_domain_offers_flattens_into_the_arrays_both_filter_twins_read()
    {
        var objectId = Guid.CreateVersion7();
        AccessEntrySnapshot[] entries =
        [
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All),
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Own),
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.CavingGroup, scopeId: GroupId),
            Entry(AccessEffect.Deny, AccessAction.Read, AccessScopeKind.Object, scopeId: objectId),
            // Another domain's rules never bleed into this slice.
            Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.All, domain: AccessDomain.TripLogs),
        ];

        var set = AccessFilterSet.Build(entries, AccessDomain.Documents, AccessAction.Read);

        set.AllowAll.ShouldBeTrue();
        set.DenyOwn.ShouldBeTrue();
        set.AllowCavingGroupIds.ShouldBe([GroupId]);
        set.DenyObjectIds.ShouldBe([objectId]);

        // The bands with no honest answer for a document must stay empty: an array only
        // the pure walk could fill would be a shape the two query forms cannot express.
        set.DenyAll.ShouldBeFalse();
        set.AllowOwn.ShouldBeFalse();
        set.AllowObjectIds.ShouldBeEmpty();
        set.DenyCavingGroupIds.ShouldBeEmpty();
        set.AllowSubtreeRoots.ShouldBeEmpty();
        set.DenySubtreeRoots.ShouldBeEmpty();
        set.AllowSetIds.ShouldBeEmpty();
        set.DenySetIds.ShouldBeEmpty();
        set.AllowAllKinds.ShouldBeEmpty();
        set.DenyAllKinds.ShouldBeEmpty();
        set.AllowOwnKinds.ShouldBeEmpty();
        set.DenyOwnKinds.ShouldBeEmpty();
        set.AllowAllTypeIds.ShouldBeEmpty();
        set.DenyAllTypeIds.ShouldBeEmpty();

        // An action nobody was granted flattens to nothing at all, which is what lets
        // both twins fall through to the lean built-ins-only predicate.
        AccessFilterSet.Build(entries, AccessDomain.Documents, AccessAction.Share).IsEmpty.ShouldBeTrue();
    }

    [Fact]
    public void An_object_entry_anchored_on_the_feature_column_reaches_no_document()
    {
        // Outside the feature domain the object anchor is the plain scope id. An entry
        // written against the feature column is not a near miss the flattener should
        // rescue — the write gate refuses it, and nothing here quietly honours it.
        var objectId = Guid.CreateVersion7();
        var misanchored = Entry(
            AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, scopeFeatureId: objectId);

        AccessFilterSet.Build([misanchored], AccessDomain.Documents, AccessAction.Read)
            .AllowObjectIds.ShouldBeEmpty();
        Allowed(Context(misanchored), Document(id: objectId)).ShouldBeFalse();

        var anchored = Entry(AccessEffect.Allow, AccessAction.Read, AccessScopeKind.Object, scopeId: objectId);
        AccessFilterSet.Build([anchored], AccessDomain.Documents, AccessAction.Read)
            .AllowObjectIds.ShouldBe([objectId]);
        Allowed(Context(anchored), Document(id: objectId)).ShouldBeTrue();
    }
}
