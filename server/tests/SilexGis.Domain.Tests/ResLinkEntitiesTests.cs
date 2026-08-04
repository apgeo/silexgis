// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

public class ResLinkEntitiesTests
{
    [Fact]
    public void The_discriminator_extension_keeps_its_numeric_contract()
    {
        // Stored as smallint — append only, never renumber.
        ((short)AttachedEntityType.Document).ShouldBe((short)9);
        ((short)AttachedEntityType.SurveyModel).ShouldBe((short)10);
        ((short)AttachedEntityType.Caver).ShouldBe((short)11);
        ((short)AttachedEntityType.Cabinet).ShouldBe((short)12);
        ((short)AttachedEntityType.Comment).ShouldBe((short)13);
    }

    [Fact]
    public void The_new_discriminator_values_name_their_audit_roots()
    {
        AttachedEntityTypes.ClrName(AttachedEntityType.Document).ShouldBe("Document");
        AttachedEntityTypes.ClrName(AttachedEntityType.SurveyModel).ShouldBe("SurveyModel");
        AttachedEntityTypes.ClrName(AttachedEntityType.Caver).ShouldBe("Caver");
        AttachedEntityTypes.ClrName(AttachedEntityType.Cabinet).ShouldBe("Cabinet");
        AttachedEntityTypes.ClrName(AttachedEntityType.Comment).ShouldBe("Comment");
    }

    [Fact]
    public void Anchor_kinds_keep_their_numeric_contract()
    {
        // Stored as smallint — append only, never renumber.
        ((short)AnchorKind.Whole).ShouldBe((short)0);
        ((short)AnchorKind.TextRange).ShouldBe((short)1);
        ((short)AnchorKind.Page).ShouldBe((short)2);
        ((short)AnchorKind.PageRange).ShouldBe((short)3);
        ((short)AnchorKind.ImageRegion).ShouldBe((short)4);
        ((short)AnchorKind.TimePoint).ShouldBe((short)5);
        ((short)AnchorKind.TimeRange).ShouldBe((short)6);
        ((short)AnchorKind.ModelStation).ShouldBe((short)7);
        ((short)AnchorKind.ModelStationRange).ShouldBe((short)8);
        ((short)AnchorKind.ModelSurvey).ShouldBe((short)9);
        ((short)AnchorKind.ModelSurveyRange).ShouldBe((short)10);
        ((short)AnchorKind.ModelPoint).ShouldBe((short)11);
        ((short)AnchorKind.Waypoint).ShouldBe((short)12);
        ((short)AnchorKind.WaypointRange).ShouldBe((short)13);
    }

    [Fact]
    public void A_member_surfaces_in_the_timeline_of_its_target()
    {
        var featureId = Guid.CreateVersion7();
        var featureMember = new ResLinkMember { ResLinkId = Guid.CreateVersion7(), FeatureId = featureId };
        featureMember.RootEntityType.ShouldBe(nameof(Feature));
        featureMember.RootEntityId.ShouldBe(featureId.ToString());

        var documentId = Guid.CreateVersion7();
        var documentMember = new ResLinkMember
        {
            ResLinkId = Guid.CreateVersion7(),
            EntityType = AttachedEntityType.Document,
            EntityId = documentId,
        };
        documentMember.RootEntityType.ShouldBe("Document");
        documentMember.RootEntityId.ShouldBe(documentId.ToString());

        var caverMember = new ResLinkMember
        {
            ResLinkId = Guid.CreateVersion7(),
            EntityType = AttachedEntityType.Caver,
            EntityId = Guid.CreateVersion7(),
        };
        caverMember.RootEntityType.ShouldBe("Caver");
    }

    [Fact]
    public void New_rows_get_ids_and_honest_defaults()
    {
        var link = new ResLink { ShortCode = "A1b2C3d4" };
        link.Id.ShouldNotBe(Guid.Empty);
        link.AuditId.ShouldBe(link.Id.ToString());
        link.RelationTypeId.ShouldBeNull();

        var member = new ResLinkMember { ResLinkId = link.Id, FeatureId = Guid.CreateVersion7() };
        member.Id.ShouldNotBe(Guid.Empty);
        member.Id.ShouldNotBe(link.Id);
        member.AnchorKind.ShouldBe(AnchorKind.Whole);
        member.Anchor.ShouldBeNull();
        member.IsMain.ShouldBeFalse();
        member.AuditId.ShouldBe(member.Id.ToString());

        var relation = new ResLinkRelationType { Code = "custom", Name = "Custom" };
        relation.Directed.ShouldBeFalse();
        relation.InverseName.ShouldBeNull();
    }
}
