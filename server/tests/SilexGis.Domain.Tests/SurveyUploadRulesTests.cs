// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Tests;

public class SurveyCaveNamingTests
{
    [Fact]
    public void A_cave_is_named_after_the_title_else_the_survey_name_and_nothing_when_both_are_blank()
    {
        SurveyCaveNaming.TitleOf("  Pestera Inventata  ", "inventata-1").ShouldBe("Pestera Inventata");
        SurveyCaveNaming.TitleOf(" ", "inventata-1").ShouldBe("inventata-1");
        SurveyCaveNaming.TitleOf(null, " ").ShouldBeNull();
        SurveyCaveNaming.TitleOf(new string('a', 300), null)!.Length.ShouldBe(SurveyCaveNaming.MaxLength);
    }

    [Fact]
    public void A_title_no_visible_cave_has_is_used_as_it_is()
    {
        SurveyCaveNaming.Unclaimed("Pestera Inventata", ["Alta Pestera", "Pestera Inventata (2)"])
            .ShouldBe("Pestera Inventata");
    }

    [Fact]
    public void A_title_a_visible_cave_has_takes_the_first_free_suffix()
    {
        SurveyCaveNaming.Unclaimed("Pestera Inventata", ["Pestera Inventata"]).ShouldBe("Pestera Inventata (2)");
        SurveyCaveNaming.Unclaimed("Pestera Inventata", ["Pestera Inventata", "Pestera Inventata (2)"])
            .ShouldBe("Pestera Inventata (3)");
        // A gap is filled rather than counted past: the suffix says "another of this name", not how many.
        SurveyCaveNaming.Unclaimed("Pestera Inventata", ["Pestera Inventata", "Pestera Inventata (3)"])
            .ShouldBe("Pestera Inventata (2)");
    }

    [Fact]
    public void Names_compare_exactly_as_written()
    {
        SurveyCaveNaming.Unclaimed("Pestera Inventata", ["pestera inventata"]).ShouldBe("Pestera Inventata");
    }

    [Fact]
    public void The_suffix_shortens_the_title_rather_than_overrunning_a_cave_name()
    {
        var title = new string('x', SurveyCaveNaming.MaxLength);

        var name = SurveyCaveNaming.Unclaimed(title, [title]);

        name.Length.ShouldBe(SurveyCaveNaming.MaxLength);
        name.ShouldEndWith(" (2)");
    }
}

public class CaveSurveyMatchingTests
{
    private static readonly Guid First = Guid.CreateVersion7();
    private static readonly Guid Second = Guid.CreateVersion7();

    private static readonly CaveSurveyKey[] Surveys =
    [
        new(First, SurveyOrigin.TopoDroid, "inventata-2026-10"),
        new(Second, SurveyOrigin.Therion, "inventata-2026-10"),
    ];

    [Fact]
    public void The_id_the_device_sent_wins_over_the_name()
    {
        CaveSurveyMatching.Match(Second, SurveyOrigin.TopoDroid, "inventata-2026-10", Surveys)
            .ShouldBe(new CaveSurveyMatch(CaveSurveyMatchKind.ById, Second));
    }

    [Fact]
    public void An_id_the_cave_does_not_have_is_not_found_rather_than_matched_by_name()
    {
        CaveSurveyMatching.Match(Guid.CreateVersion7(), SurveyOrigin.TopoDroid, "inventata-2026-10", Surveys)
            .Kind.ShouldBe(CaveSurveyMatchKind.NotFound);
    }

    [Fact]
    public void Without_an_id_the_same_tool_and_name_find_the_survey()
    {
        CaveSurveyMatching.Match(null, SurveyOrigin.TopoDroid, " inventata-2026-10 ", Surveys)
            .ShouldBe(new CaveSurveyMatch(CaveSurveyMatchKind.ByExternalKey, First));
        CaveSurveyMatching.Match(null, SurveyOrigin.Therion, "inventata-2026-10", Surveys)
            .ShouldBe(new CaveSurveyMatch(CaveSurveyMatchKind.ByExternalKey, Second));
    }

    [Theory]
    [InlineData("Inventata-2026-10")]
    [InlineData("inventata-2026-11")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_starts_a_new_survey(string? name)
    {
        CaveSurveyMatching.Match(null, SurveyOrigin.TopoDroid, name, Surveys)
            .ShouldBe(new CaveSurveyMatch(CaveSurveyMatchKind.New));
    }

    [Fact]
    public void A_survey_with_no_name_is_never_matched_by_name()
    {
        CaveSurveyKey[] unnamed = [new(First, SurveyOrigin.TopoDroid, null)];

        CaveSurveyMatching.Match(null, SurveyOrigin.TopoDroid, null, unnamed).Kind.ShouldBe(CaveSurveyMatchKind.New);
        CaveSurveyMatching.Match(null, SurveyOrigin.TopoDroid, "  ", unnamed).Kind.ShouldBe(CaveSurveyMatchKind.New);
    }
}

public class SurveyCaveVisibilityTests
{
    private static readonly Guid Club = Guid.CreateVersion7();
    private static readonly Guid Other = Guid.CreateVersion7();

    [Fact]
    public void Unasked_the_cave_opens_to_the_uploaders_club_when_there_is_one_and_is_private_otherwise()
    {
        SurveyCaveVisibility.Resolve(null, null, [Club], null)
            .ShouldBe(new SurveyCaveAudience(Visibility.CavingGroup, Club));
        SurveyCaveVisibility.Resolve(null, null, [], null)
            .ShouldBe(new SurveyCaveAudience(Visibility.Private, null));
    }

    [Fact]
    public void Among_several_groups_the_accounts_own_club_is_the_one_and_without_it_none_is_guessed()
    {
        SurveyCaveVisibility.Resolve(null, null, [Other, Club], Club)
            .ShouldBe(new SurveyCaveAudience(Visibility.CavingGroup, Club));
        SurveyCaveVisibility.Resolve(null, null, [Other, Club], null)
            .ShouldBe(new SurveyCaveAudience(Visibility.Private, null));
        // A profile club the account is no longer a member of is not a club it can open a cave to.
        SurveyCaveVisibility.DefaultGroup([Other], Club).ShouldBe(Other);
    }

    [Theory]
    [InlineData(Visibility.Private)]
    [InlineData(Visibility.Authenticated)]
    [InlineData(Visibility.Public)]
    public void Every_visibility_may_be_asked_for_and_a_defaulted_group_is_then_not_bound(Visibility asked)
    {
        SurveyCaveVisibility.Resolve(asked, null, [Club], null)
            .ShouldBe(new SurveyCaveAudience(asked, null));
    }

    [Fact]
    public void A_group_named_outright_is_bound_whatever_the_visibility()
    {
        SurveyCaveVisibility.Resolve(Visibility.Public, Other, [Club], null)
            .ShouldBe(new SurveyCaveAudience(Visibility.Public, Other));
        SurveyCaveVisibility.Resolve(Visibility.CavingGroup, Other, [Club, Other], null)
            .ShouldBe(new SurveyCaveAudience(Visibility.CavingGroup, Other));
    }

    [Fact]
    public void A_group_cave_with_no_group_to_open_to_is_refused_with_the_reason()
    {
        SurveyCaveVisibility.Resolve(Visibility.CavingGroup, null, [], null).RefusalCode
            .ShouldBe(SurveyCaveVisibility.InvalidCode);
        SurveyCaveVisibility.Resolve(Visibility.CavingGroup, null, [Club, Other], null).RefusalCode
            .ShouldBe(SurveyCaveVisibility.GroupRequiredCode);
        SurveyCaveVisibility.Resolve(Visibility.CavingGroup, null, [Club], null).Refused.ShouldBeFalse();
    }
}

public class SurveyVersionStampTests
{
    private static readonly DateTimeOffset Opened = new(2026, 10, 25, 1, 32, 0, 456, TimeSpan.Zero);

    [Fact]
    public void A_version_is_the_opening_moment_to_the_whole_second_in_utc()
    {
        SurveyVersionStamp.Next(Opened, null).ShouldBe(new DateTimeOffset(2026, 10, 25, 1, 32, 0, TimeSpan.Zero));
        SurveyVersionStamp.Next(new DateTimeOffset(2026, 10, 25, 4, 32, 0, TimeSpan.FromHours(3)), null).Offset
            .ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void A_version_is_always_after_the_surveys_latest_so_two_opens_in_one_second_get_two_stamps()
    {
        var first = SurveyVersionStamp.Next(Opened, null);

        var second = SurveyVersionStamp.Next(Opened.AddMilliseconds(300), first);

        second.ShouldBe(first.AddSeconds(1));
        SurveyVersionStamp.Next(Opened.AddMinutes(5), first).ShouldBe(first.AddMinutes(5));
    }

    [Fact]
    public void The_stamp_and_the_artefact_name_carry_the_version()
    {
        SurveyVersionStamp.Of(Opened).ShouldBe("20261025T013200Z");
        SurveyVersionStamp.ArtefactName("inventata-2026-10", Opened, ".LOX").ShouldBe("inventata-2026-10-20261025T013200Z.lox");
        SurveyVersionStamp.ArtefactName("inventata-2026-10", Opened, "pdf").ShouldBe("inventata-2026-10-20261025T013200Z.pdf");
    }

    [Theory]
    [InlineData("a/b\\c:d", "a-b-c-d")]
    [InlineData("..hidden. ", "hidden")]
    [InlineData("  ", "survey")]
    [InlineData("tab\there", "tab-here")]
    public void The_survey_part_is_made_safe_for_a_file_name_on_any_system(string survey, string stem)
    {
        SurveyVersionStamp.ArtefactName(survey, Opened, "lox").ShouldBe($"{stem}-20261025T013200Z.lox");
    }

    [Fact]
    public void A_long_survey_name_is_shortened_and_the_stamp_and_extension_are_kept()
    {
        var name = SurveyVersionStamp.ArtefactName(new string('s', 400), Opened, "plan.pdf");

        name.Length.ShouldBe(SurveyVersionStamp.MaxFileNameLength);
        name.ShouldEndWith("-20261025T013200Z.plan.pdf");
    }

    [Fact]
    public void A_document_title_carries_the_date_and_time()
    {
        SurveyVersionStamp.DocumentTitle("Pestera Inventata — galeria noua", Opened)
            .ShouldBe("Pestera Inventata — galeria noua — 2026-10-25 01:32:00 UTC");
        SurveyVersionStamp.DocumentTitle(new string('t', 400), Opened).Length.ShouldBe(SurveyVersionStamp.MaxTitleLength);
    }
}

public class SurveyOutputsTests
{
    [Fact]
    public void Nothing_asked_for_means_the_defaults_and_the_accounts_and_installations_lists_are_added()
    {
        SurveyOutputs.Effective(null, SurveyOutputs.Defaults, ["kml"], ["dxf"])
            .ShouldBe(["lox", "3d", "plan-pdf", "extended-pdf", "kml", "dxf"]);
        SurveyOutputs.Effective([], SurveyOutputs.Defaults, [], [])
            .ShouldBe(SurveyOutputs.Defaults);
    }

    [Fact]
    public void A_list_asked_for_replaces_the_defaults_but_never_the_accounts_or_installations()
    {
        SurveyOutputs.Effective(["plan-svg"], SurveyOutputs.Defaults, ["lox"], ["3d"])
            .ShouldBe(["lox", "3d", "plan-svg"]);
    }

    [Fact]
    public void Each_output_is_named_once_in_catalogue_order_whatever_the_case_and_unknown_names_are_dropped()
    {
        SurveyOutputs.Effective(["KML", "lox", "kml"], [], ["LOX", "no-such-output"], ["shp", " csv "])
            .ShouldBe(["lox", "kml", "csv", "shp"]);
    }

    [Fact]
    public void Unknown_names_in_a_request_are_reported_for_refusal()
    {
        SurveyOutputs.Unknown(["lox", "Plan-PDF", "pdf", "pdf", null]).ShouldBe(["pdf", ""]);
        SurveyOutputs.IsKnown(" 3D ").ShouldBeTrue();
    }

    [Fact]
    public void A_setting_is_read_as_a_trimmed_lower_case_list()
    {
        SurveyOutputs.Parse(" lox, 3D ,,plan-pdf,lox").ShouldBe(["lox", "3d", "plan-pdf"]);
        SurveyOutputs.Parse(null).ShouldBeEmpty();
        SurveyOutputs.Parse("").ShouldBeEmpty();
    }

    [Fact]
    public void The_defaults_are_catalogue_members()
    {
        SurveyOutputs.Defaults.ShouldAllBe(o => SurveyOutputs.Catalogue.Contains(o));
        SurveyOutputs.Catalogue.Distinct().Count().ShouldBe(SurveyOutputs.Catalogue.Count);
    }
}

public class SurveyDocumentAudienceTests
{
    private static readonly Guid CallerId = Guid.CreateVersion7();
    private static readonly Guid Club = Guid.CreateVersion7();
    private static readonly Guid Elsewhere = Guid.CreateVersion7();

    private static AccessEntrySnapshot Allow(AccessAction actions, AccessScopeKind scope, Guid? scopeId = null) => new(
        1, Guid.CreateVersion7(), null, null, AccessEffect.Allow, AccessDomain.Documents, actions, scope, null, scopeId, null, null);

    /// <summary>Somebody who may create documents anywhere, in the given groups.</summary>
    private static AccessContext Creator(params Guid[] clubs) =>
        new(CallerId, false, clubs, [Allow(AccessAction.Read | AccessAction.Write | AccessAction.Create, AccessScopeKind.All)]);

    [Fact]
    public void A_member_of_the_caves_club_files_the_document_under_the_club_on_the_caves_terms()
    {
        SurveyDocumentAudience.Resolve(Creator(Club), false, Visibility.CavingGroup, Club, null)
            .ShouldBe(new SurveyCaveAudience(Visibility.CavingGroup, Club));
        SurveyDocumentAudience.Resolve(Creator(Club), false, Visibility.CavingGroup, Club, Visibility.Authenticated)
            .ShouldBe(new SurveyCaveAudience(Visibility.Authenticated, Club));
    }

    [Fact]
    public void A_writer_outside_the_club_cannot_bind_the_document_to_it()
    {
        // Inherited from the cave, a group visibility with no group to open to becomes private.
        SurveyDocumentAudience.Resolve(Creator(Elsewhere), false, Visibility.CavingGroup, Club, null)
            .ShouldBe(new SurveyCaveAudience(Visibility.Private, null));
        // Asked for outright, it is refused with the reason rather than quietly narrowed.
        SurveyDocumentAudience.Resolve(Creator(Elsewhere), false, Visibility.Authenticated, Club, Visibility.CavingGroup)
            .RefusalCode.ShouldBe(SurveyCaveVisibility.InvalidCode);
        // Another visibility keeps, unbound.
        SurveyDocumentAudience.Resolve(Creator(Elsewhere), false, Visibility.Authenticated, Club, null)
            .ShouldBe(new SurveyCaveAudience(Visibility.Authenticated, null));
    }

    [Fact]
    public void A_group_document_asked_for_on_a_cave_with_no_group_is_refused()
    {
        SurveyDocumentAudience.Resolve(Creator(Club), false, Visibility.Authenticated, null, Visibility.CavingGroup)
            .RefusalCode.ShouldBe(SurveyCaveVisibility.InvalidCode);
    }

    [Fact]
    public void Somebody_who_may_create_no_documents_is_refused_whatever_they_may_do_to_the_cave()
    {
        var writerOnly = new AccessContext(CallerId, false, [Elsewhere], []);

        SurveyDocumentAudience.Resolve(writerOnly, false, Visibility.CavingGroup, Club, null)
            .RefusalCode.ShouldBe(CreateRules.ForbiddenCode);
        SurveyDocumentAudience.Resolve(writerOnly, true, Visibility.Authenticated, null, null)
            .RefusalCode.ShouldBe(CreateRules.ForbiddenCode);
    }

    [Fact]
    public void A_club_ruleset_that_grants_documents_only_in_the_club_is_enough_to_file_there()
    {
        var member = new AccessContext(
            CallerId, false, [Club], [Allow(AccessAction.Read | AccessAction.Write | AccessAction.Create, AccessScopeKind.CavingGroup, Club)]);

        SurveyDocumentAudience.Resolve(member, false, Visibility.CavingGroup, Club, null)
            .ShouldBe(new SurveyCaveAudience(Visibility.CavingGroup, Club));
    }

    [Fact]
    public void On_a_protected_cave_the_document_stays_the_uploaders_own_and_unbound()
    {
        SurveyDocumentAudience.Resolve(Creator(Club), true, Visibility.Public, Club, Visibility.Public)
            .ShouldBe(new SurveyCaveAudience(Visibility.Private, null));
    }
}

public class SurveyUploadArtefactsTests
{
    private static readonly DateTimeOffset Version = new(2026, 10, 25, 1, 32, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("lox", "inventata-20261025T013200Z.lox", SurveyArtefactStorage.LinePlot)]
    [InlineData("3d", "inventata-20261025T013200Z.3d", SurveyArtefactStorage.Survex)]
    [InlineData("plan-pdf", "inventata-20261025T013200Z.plan.pdf", SurveyArtefactStorage.Document)]
    [InlineData("extended-svg", "inventata-20261025T013200Z.extended.svg", SurveyArtefactStorage.Document)]
    [InlineData("shp", "inventata-20261025T013200Z.shp.zip", SurveyArtefactStorage.Document)]
    public void Every_output_is_named_for_its_version_and_stored_as_what_it_is(string output, string name, SurveyArtefactStorage storage)
    {
        var kind = SurveyUploadArtefacts.KindOf(output).ShouldNotBeNull();

        SurveyUploadArtefacts.FileName("inventata", Version, kind).ShouldBe(name);
        kind.Storage.ShouldBe(storage);
    }

    [Fact]
    public void Every_output_of_the_catalogue_has_a_name_of_its_own()
    {
        var names = SurveyOutputs.Catalogue
            .Select(o => SurveyUploadArtefacts.FileName("inventata", Version, SurveyUploadArtefacts.KindOf(o).ShouldNotBeNull()))
            .Append(SurveyUploadArtefacts.LogFileName("inventata", Version))
            .Append(SurveyUploadArtefacts.ConfigFileName("inventata", Version))
            .Append(SurveyUploadArtefacts.WrapperFileName("inventata", Version))
            .ToList();

        names.Distinct(StringComparer.OrdinalIgnoreCase).Count().ShouldBe(names.Count);
        SurveyUploadArtefacts.KindOf("nothing").ShouldBeNull();
    }

    [Fact]
    public void Nothing_is_pruned_by_default_and_never_the_newest_versions_or_one_still_running()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var d = Guid.NewGuid();
        (Guid, DateTimeOffset, bool, bool)[] versions =
        [
            (a, Version, true, true), (b, Version.AddHours(1), false, true), (c, Version.AddHours(2), true, true),
            (d, Version.AddHours(3), true, true),
        ];

        SurveyUploadArtefacts.Prunable(versions, 0).ShouldBeEmpty();
        SurveyUploadArtefacts.Prunable(versions, 1).ShouldBe([c, a]);
        SurveyUploadArtefacts.Prunable(versions, 3).ShouldBe([a]);
        SurveyUploadArtefacts.Prunable(versions, 4).ShouldBeEmpty();
    }

    [Fact]
    public void A_newer_version_that_produced_nothing_does_not_take_the_place_of_the_last_that_did()
    {
        var good = Guid.NewGuid();
        var older = Guid.NewGuid();
        (Guid, DateTimeOffset, bool, bool)[] versions =
        [
            (older, Version, true, true), (good, Version.AddHours(1), true, true),
            // Abandoned on the way, and failed to compile: neither holds anything.
            (Guid.NewGuid(), Version.AddHours(2), true, false), (Guid.NewGuid(), Version.AddHours(3), true, false),
        ];

        SurveyUploadArtefacts.Prunable(versions, 1).ShouldBe([older]);
    }

    [Theory]
    [InlineData("UTM35", 32635)]
    [InlineData("UTM35N", 32635)]
    [InlineData("utm34s", 32734)]
    [InlineData("EPSG:31700", 31700)]
    [InlineData("lat-long", 4326)]
    [InlineData("UTM61", null)]
    [InlineData("S-JTSK", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void A_coordinate_system_as_Therion_names_it_is_read_as_its_EPSG_code(string? cs, int? epsg)
    {
        TherionCoordinateSystems.EpsgOf(cs).ShouldBe(epsg);
    }

    [Theory]
    [InlineData("lox", false, false)]
    [InlineData("3d", false, false)]
    [InlineData("lox", true, true)]
    [InlineData("3d", true, true)]
    [InlineData("plan-pdf", false, true)]
    [InlineData(null, false, true)]
    public void A_model_output_whose_model_was_deleted_is_no_longer_listed(string? output, bool hasModel, bool listed)
    {
        SurveyUploadArtefacts.Listable(output, hasModel ? Guid.NewGuid() : null).ShouldBe(listed);
    }

    [Fact]
    public void Only_the_per_output_codes_have_an_artefact_sentence()
    {
        SurveyPipelineNotes.ForArtefact(SurveyPipelineFailures.OutputMissing).ShouldNotBeNullOrEmpty();
        SurveyPipelineNotes.ForArtefact(SurveyPipelineFailures.CrsUnknown).ShouldNotBeNullOrEmpty();
        SurveyPipelineNotes.ForArtefact(SurveyPipelineFailures.CompileFailed).ShouldBeNull();
    }
}
