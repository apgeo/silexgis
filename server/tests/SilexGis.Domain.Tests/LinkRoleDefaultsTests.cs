// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

public sealed class LinkRoleDefaultsTests
{
    [Theory]
    [InlineData(LinkTargetNature.Entrance, AttachmentRole.PhotoEntrance)]
    [InlineData(LinkTargetNature.CaveInterior, AttachmentRole.PhotoInterior)]
    [InlineData(LinkTargetNature.Cave, AttachmentRole.Other)]
    [InlineData(LinkTargetNature.Recording, AttachmentRole.Other)]
    [InlineData(LinkTargetNature.Other, AttachmentRole.Other)]
    public void A_picture_takes_its_role_from_where_it_hangs(LinkTargetNature target, AttachmentRole expected) =>
        LinkRoleDefaults.For(FileKind.Image, target).ShouldBe(expected);

    [Theory]
    [InlineData(FileKind.Document)]
    [InlineData(FileKind.Video)]
    [InlineData(FileKind.Audio)]
    [InlineData(FileKind.Other)]
    public void Anything_that_is_not_a_picture_is_a_document_wherever_it_hangs(FileKind kind)
    {
        foreach (var target in Enum.GetValues<LinkTargetNature>())
        {
            LinkRoleDefaults.For(kind, target).ShouldBe(AttachmentRole.Document);
        }
    }

    [Theory]
    [InlineData(FeatureKind.CaveEntrance, null, LinkTargetNature.Entrance)]
    [InlineData(FeatureKind.Cave, null, LinkTargetNature.Cave)]
    [InlineData(FeatureKind.Generic, "cave_place", LinkTargetNature.CaveInterior)]
    [InlineData(FeatureKind.Generic, "cave_area", LinkTargetNature.CaveInterior)]
    [InlineData(FeatureKind.Generic, "surface_area", LinkTargetNature.Other)]
    [InlineData(FeatureKind.Generic, null, LinkTargetNature.Other)]
    [InlineData(FeatureKind.Centerline, null, LinkTargetNature.Other)]
    public void A_feature_is_classed_by_its_kind_and_for_a_generic_one_its_type(
        FeatureKind kind, string? typeCode, LinkTargetNature expected) =>
        LinkRoleDefaults.NatureOf(kind, typeCode).ShouldBe(expected);
}
