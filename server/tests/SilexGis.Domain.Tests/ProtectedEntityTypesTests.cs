// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Which polymorphic discriminator names a protected row. A row missing from this map is
/// not "untyped" — the call throws, which is a server fault rather than a refusal, so a
/// governed entity that any polymorphic surface can reach has to be in it.
/// </summary>
public class ProtectedEntityTypesTests
{
    [Fact]
    public void A_trip_is_named_by_its_own_discriminator()
    {
        var trip = new TripLog { Title = "Sump dive", OwnerUserId = Guid.CreateVersion7() };

        ProtectedEntityTypes.Of(trip).ShouldBe(AttachedEntityType.TripLog);
    }

    [Fact]
    public void A_camp_is_named_by_its_own_discriminator()
    {
        var expedition = new Expedition { Name = "Summer camp", OwnerUserId = Guid.CreateVersion7() };

        // A camp takes files, tags and links and is shared per object, so every one of those
        // surfaces asks this question about it. Before the camp had a value of its own the
        // question threw, which reaches a caller as a fault instead of an answer.
        ProtectedEntityTypes.Of(expedition).ShouldBe(AttachedEntityType.Expedition);
    }

    [Fact]
    public void An_entity_with_no_discriminator_is_refused_rather_than_defaulted()
    {
        // Falling back to some default value would file rows about an unknown row under
        // another kind's identity, where the next reader would resolve them against the
        // wrong table. Throwing keeps the omission visible.
        Should.Throw<ArgumentException>(() => ProtectedEntityTypes.Of(new UntypedRow()));
    }

    [Fact]
    public void The_stored_value_of_a_camp_is_the_appended_one()
    {
        // The numbering is a schema contract: the value is what is stored in the smallint
        // column, so it is appended and never renumbered. Pinned as a literal because a
        // renumbering would otherwise pass every other test in the suite while silently
        // repointing every row already written.
        ((short)AttachedEntityType.Expedition).ShouldBe((short)15);
    }

    [Fact]
    public void A_camps_audit_root_name_is_the_name_its_children_write()
    {
        // Rows that hang off a camp name their root by the CLR type name; the timeline
        // matches the two as strings, so a mismatch here is a timeline that finds nothing.
        AttachedEntityTypes.ClrName(AttachedEntityType.Expedition).ShouldBe(nameof(Expedition));
    }

    private sealed class UntypedRow : IProtectedEntity
    {
        public Guid Id { get; set; } = Guid.CreateVersion7();

        public Guid OwnerUserId { get; set; }

        public Guid? CavingGroupId { get; set; }

        public Visibility Visibility { get; set; } = Visibility.Private;
    }
}
