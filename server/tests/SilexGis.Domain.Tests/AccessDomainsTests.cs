// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which resource domain governs a protected row. An entity missing from this map is not
/// "ungoverned" — every call that asks for its domain throws, so the gap surfaces as a
/// failure rather than as an accidental grant.
/// </summary>
public class AccessDomainsTests
{
    [Fact]
    public void A_document_is_governed_by_the_documents_domain()
    {
        var document = new Document { Title = "Survey report", OwnerUserId = Guid.CreateVersion7() };

        AccessDomains.Of(document).ShouldBe(AccessDomain.Documents);
    }

    [Fact]
    public void An_expedition_is_governed_by_its_own_domain_and_not_by_the_trips_inside_it()
    {
        var expedition = new Expedition { Name = "Summer camp", OwnerUserId = Guid.CreateVersion7() };

        // Not the trip-log domain: an entry scoped to one object resolves what it is anchored
        // to against the table its domain names, so an expedition id written under the trips
        // would name nothing and the entry would be refused. Sharing one camp with a partner
        // club is exactly such an entry, and it is what the entity exists for.
        AccessDomains.Of(expedition).ShouldBe(AccessDomain.Expeditions);
        AccessDomains.Of(expedition).ShouldNotBe(AccessDomain.TripLogs);
    }

    [Fact]
    public void A_checklist_is_governed_by_its_own_domain_and_not_by_the_trips_that_use_it()
    {
        var checklist = new Checklist { Title = "Before we set off", OwnerUserId = Guid.CreateVersion7() };

        // Not the trip-log domain, and the two halves of that are separate refusals. An entry
        // scoped to one object resolves what it is anchored to against the table its domain
        // names, so a checklist id written under the trips would name nothing and the entry
        // would be refused — and "share this list with them" is exactly such an entry. The only
        // shape left, a grant over every trip, is a single flag: it would hand its holder every
        // checklist in the installation, private ones included.
        AccessDomains.Of(checklist).ShouldBe(AccessDomain.Checklists);
        AccessDomains.Of(checklist).ShouldNotBe(AccessDomain.TripLogs);

        // The trio scopes only mean anything where the row actually carries the columns, and a
        // checklist does — which is what lets an author keep their own lists and a club share
        // one without an entry per row.
        AccessEntryRules.IsTrioDomain(AccessDomain.Checklists).ShouldBeTrue();
        AccessEntryRules.AllowsObjectScope(AccessDomain.Checklists).ShouldBeTrue();
    }

    [Fact]
    public void An_entity_with_no_domain_is_refused_rather_than_defaulted()
    {
        // Falling back to some default domain would authorize an unknown row against a
        // table written for a different one; throwing keeps the omission visible.
        Should.Throw<ArgumentException>(() => AccessDomains.Of(new UngovernedRow()));
    }

    private sealed class UngovernedRow : IProtectedEntity
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();

        public Guid OwnerUserId { get; set; }

        public Guid? CavingGroupId { get; set; }

        public Visibility Visibility { get; set; } = Visibility.Private;
    }
}
