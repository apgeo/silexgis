// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain;
using SilexGis.Domain.Documents;
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Tests;

/// <summary>
/// What a shelf says about whatever lands on it, and how that combines with what the shelves
/// above it say.
///
/// <para>
/// The asymmetry between the two halves is the point and is tested from both sides: the
/// single-valued settings are answered by the nearest shelf that holds an opinion, so a
/// corner of an archive can override its parent; the expected metadata keys accumulate, so a
/// requirement written on the archive is not shed by a sub-shelf that lists none of its own.
/// </para>
/// </summary>
public class CabinetDefaultsTests
{
    private static readonly Guid ArchiveId = Guid.CreateVersion7();
    private static readonly Guid BulletinsId = Guid.CreateVersion7();
    private static readonly Guid PrivateSurveysId = Guid.CreateVersion7();

    private static Cabinet Cabinet(
        Guid id,
        Guid[] ancestorIds,
        long? typeId = null,
        Visibility? visibility = null,
        long[]? tagIds = null,
        string[]? requiredKeys = null) =>
        new()
        {
            Id = id,
            Name = id.ToString(),
            AncestorIds = ancestorIds,
            DefaultDocumentTypeId = typeId,
            DefaultVisibility = visibility,
            DefaultTagIds = tagIds ?? [],
            RequiredMetadataKeys = requiredKeys ?? [],
        };

    /// <summary>Archive → Bulletins → Private surveys, each stamped with its own ancestry.</summary>
    private static List<Cabinet> Chain(
        Cabinet? archive = null, Cabinet? bulletins = null, Cabinet? privateSurveys = null) =>
        [
            archive ?? Cabinet(ArchiveId, [ArchiveId]),
            bulletins ?? Cabinet(BulletinsId, [ArchiveId, BulletinsId]),
            privateSurveys ?? Cabinet(PrivateSurveysId, [ArchiveId, BulletinsId, PrivateSurveysId]),
        ];

    [Fact]
    public void A_shelf_that_says_nothing_and_whose_ancestors_say_nothing_answers_nothing()
    {
        var defaults = CabinetDefaultRules.Resolve(BulletinsId, Chain());

        defaults.DocumentTypeId.ShouldBeNull();
        defaults.Visibility.ShouldBeNull();
        defaults.TagIds.ShouldBeEmpty();
        defaults.RequiredMetadataKeys.ShouldBeEmpty();
    }

    [Fact]
    public void An_unknown_cabinet_answers_nothing_rather_than_throwing()
    {
        // The shelf may have been deleted between the upload being composed and it arriving.
        CabinetDefaultRules.Resolve(Guid.CreateVersion7(), Chain()).ShouldBe(CabinetDefaults.None);
        CabinetDefaultRules.Resolve(BulletinsId, []).ShouldBe(CabinetDefaults.None);
    }

    [Fact]
    public void A_setting_written_high_up_reaches_everything_below_it()
    {
        var chain = Chain(archive: Cabinet(
            ArchiveId, [ArchiveId], typeId: 7, visibility: Visibility.CavingGroup));

        var defaults = CabinetDefaultRules.Resolve(PrivateSurveysId, chain);

        defaults.DocumentTypeId.ShouldBe(7);
        defaults.Visibility.ShouldBe(Visibility.CavingGroup);
    }

    [Fact]
    public void The_nearest_shelf_with_an_opinion_wins_over_the_ones_above_it()
    {
        var chain = Chain(
            archive: Cabinet(ArchiveId, [ArchiveId], typeId: 7, visibility: Visibility.CavingGroup),
            privateSurveys: Cabinet(
                PrivateSurveysId,
                [ArchiveId, BulletinsId, PrivateSurveysId],
                visibility: Visibility.Private));

        var defaults = CabinetDefaultRules.Resolve(PrivateSurveysId, chain);

        // Its own answer for what it has an opinion about...
        defaults.Visibility.ShouldBe(Visibility.Private);
        // ...and the archive's for what it does not.
        defaults.DocumentTypeId.ShouldBe(7);
    }

    [Fact]
    public void Private_is_a_real_opinion_and_not_the_absence_of_one()
    {
        // The reason the column is nullable rather than defaulting to Private: a shelf saying
        // "everything here is private" must override a club-visible archive above it, and
        // would be indistinguishable from silence if absence were spelled the same way.
        var chain = Chain(
            archive: Cabinet(ArchiveId, [ArchiveId], visibility: Visibility.Public),
            bulletins: Cabinet(BulletinsId, [ArchiveId, BulletinsId], visibility: Visibility.Private));

        CabinetDefaultRules.Resolve(BulletinsId, chain).Visibility.ShouldBe(Visibility.Private);
    }

    [Fact]
    public void Tags_gather_down_the_chain_nearest_first_and_are_not_repeated()
    {
        var chain = Chain(
            archive: Cabinet(ArchiveId, [ArchiveId], tagIds: [1, 2]),
            bulletins: Cabinet(BulletinsId, [ArchiveId, BulletinsId], tagIds: [3, 1]));

        CabinetDefaultRules.Resolve(BulletinsId, chain).TagIds.ShouldBe(new long[] { 3, 1, 2 });
    }

    [Fact]
    public void Expected_metadata_keys_accumulate_rather_than_being_answered_by_the_nearest_shelf()
    {
        // A requirement on the archive governs everything inside it; a sub-shelf adding one of
        // its own is narrowing, not replacing. Answering this the way the single-valued
        // settings are answered would let any sub-shelf quietly excuse its documents.
        var chain = Chain(
            archive: Cabinet(ArchiveId, [ArchiveId], requiredKeys: ["author", "year"]),
            bulletins: Cabinet(BulletinsId, [ArchiveId, BulletinsId], requiredKeys: ["issue", "author"]));

        CabinetDefaultRules.Resolve(BulletinsId, chain).RequiredMetadataKeys
            .ShouldBe(new[] { "issue", "author", "year" });
    }

    [Fact]
    public void A_shelf_is_resolved_from_its_own_settings_even_when_its_ancestry_is_unstamped()
    {
        // A cabinet whose ancestor array has not been written yet still answers with what it
        // itself says, rather than answering nothing.
        var orphan = Cabinet(BulletinsId, [], typeId: 3);

        CabinetDefaultRules.Resolve(BulletinsId, [orphan]).DocumentTypeId.ShouldBe(3);
    }

    [Fact]
    public void Missing_keys_are_the_expected_ones_the_document_does_not_answer()
    {
        CabinetDefaultRules.MissingMetadataKeys(["author", "year"], ["author"]).ShouldBe(new[] { "year" });
        CabinetDefaultRules.MissingMetadataKeys(["author"], ["author", "year"]).ShouldBeEmpty();
        CabinetDefaultRules.MissingMetadataKeys([], ["author"]).ShouldBeEmpty();
        CabinetDefaultRules.MissingMetadataKeys(["author"], []).ShouldBe(new[] { "author" });
    }
}
