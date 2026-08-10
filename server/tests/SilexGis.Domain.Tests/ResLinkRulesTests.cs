// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using static SilexGis.Domain.ResLinks.ResLinkRules;

namespace SilexGis.Domain.Tests;

public class ResLinkRulesTests
{
    private static readonly Guid SomeId = Guid.CreateVersion7();

    // ---- target shape -------------------------------------------------------------

    [Fact]
    public void Exactly_one_target_shape_is_valid()
    {
        TargetShapeValid(SomeId, null, null).ShouldBeTrue();
        TargetShapeValid(null, AttachedEntityType.Document, SomeId).ShouldBeTrue();

        TargetShapeValid(null, null, null).ShouldBeFalse();
        TargetShapeValid(SomeId, AttachedEntityType.Document, SomeId).ShouldBeFalse();
        TargetShapeValid(SomeId, AttachedEntityType.Document, null).ShouldBeFalse();
        TargetShapeValid(SomeId, null, SomeId).ShouldBeFalse();
        TargetShapeValid(null, AttachedEntityType.Document, null).ShouldBeFalse();
        TargetShapeValid(null, null, SomeId).ShouldBeFalse();
    }

    // ---- the (member type × anchor kind) matrix -----------------------------------

    [Theory]
    [InlineData(AttachedEntityType.TripLog)]
    [InlineData(AttachedEntityType.CavingGroup)]
    [InlineData(AttachedEntityType.Geofile)]
    [InlineData(AttachedEntityType.MapView)]
    [InlineData(AttachedEntityType.Document)]
    [InlineData(AttachedEntityType.SurveyModel)]
    [InlineData(AttachedEntityType.Caver)]
    [InlineData(AttachedEntityType.Cabinet)]
    public void Linkable_types_may_join_and_admit_whole(AttachedEntityType type)
    {
        IsLinkableType(type).ShouldBeTrue();
        AdmitsAnchor(type, AnchorKind.Whole).ShouldBeTrue();
    }

    [Theory]
    [InlineData(AttachedEntityType.StoredFile)]
    [InlineData(AttachedEntityType.GeoreferencedMap)]
    [InlineData(AttachedEntityType.Comment)]
    [InlineData((AttachedEntityType)99)]
    public void Non_linkable_types_are_refused_even_for_whole(AttachedEntityType type)
    {
        IsLinkableType(type).ShouldBeFalse();
        AdmitsAnchor(type, AnchorKind.Whole).ShouldBeFalse();
    }

    [Fact]
    public void Features_admit_only_whole()
    {
        AdmitsAnchor(null, AnchorKind.Whole).ShouldBeTrue();
        AdmitsAnchor(null, AnchorKind.Page).ShouldBeFalse();
        AdmitsAnchor(null, AnchorKind.Waypoint).ShouldBeFalse();
    }

    [Theory]
    [InlineData(AnchorKind.Whole)]
    [InlineData(AnchorKind.TextRange)]
    [InlineData(AnchorKind.Page)]
    [InlineData(AnchorKind.PageRange)]
    [InlineData(AnchorKind.ImageRegion)]
    [InlineData(AnchorKind.TimePoint)]
    [InlineData(AnchorKind.TimeRange)]
    [InlineData(AnchorKind.ModelPoint)]
    public void Documents_admit_content_anchors(AnchorKind kind) =>
        AdmitsAnchor(AttachedEntityType.Document, kind).ShouldBeTrue();

    [Theory]
    [InlineData(AnchorKind.ModelStation)]
    [InlineData(AnchorKind.ModelStationRange)]
    [InlineData(AnchorKind.ModelSurvey)]
    [InlineData(AnchorKind.ModelSurveyRange)]
    [InlineData(AnchorKind.Waypoint)]
    [InlineData(AnchorKind.WaypointRange)]
    [InlineData((AnchorKind)99)]
    public void Documents_refuse_foreign_anchors(AnchorKind kind) =>
        AdmitsAnchor(AttachedEntityType.Document, kind).ShouldBeFalse();

    [Theory]
    [InlineData(AnchorKind.ModelStation, true)]
    [InlineData(AnchorKind.ModelStationRange, true)]
    [InlineData(AnchorKind.ModelSurvey, true)]
    [InlineData(AnchorKind.ModelSurveyRange, true)]
    [InlineData(AnchorKind.Page, false)]
    [InlineData(AnchorKind.TextRange, false)]
    [InlineData(AnchorKind.Waypoint, false)]
    [InlineData(AnchorKind.ModelPoint, false)]
    public void Survey_models_admit_station_and_survey_anchors(AnchorKind kind, bool admitted) =>
        AdmitsAnchor(AttachedEntityType.SurveyModel, kind).ShouldBe(admitted);

    [Theory]
    [InlineData(AnchorKind.Waypoint, true)]
    [InlineData(AnchorKind.WaypointRange, true)]
    [InlineData(AnchorKind.TimePoint, false)]
    [InlineData(AnchorKind.ModelSurvey, false)]
    [InlineData(AnchorKind.Page, false)]
    public void Geofiles_admit_waypoint_anchors(AnchorKind kind, bool admitted) =>
        AdmitsAnchor(AttachedEntityType.Geofile, kind).ShouldBe(admitted);

    [Theory]
    [InlineData(AttachedEntityType.TripLog)]
    [InlineData(AttachedEntityType.CavingGroup)]
    [InlineData(AttachedEntityType.MapView)]
    [InlineData(AttachedEntityType.Caver)]
    [InlineData(AttachedEntityType.Cabinet)]
    public void Whole_only_types_refuse_every_part_anchor(AttachedEntityType type)
    {
        AdmitsAnchor(type, AnchorKind.Page).ShouldBeFalse();
        AdmitsAnchor(type, AnchorKind.TextRange).ShouldBeFalse();
        AdmitsAnchor(type, AnchorKind.Waypoint).ShouldBeFalse();
    }

    // ---- anchor payloads ----------------------------------------------------------

    [Fact]
    public void Whole_carries_no_payload_and_every_other_kind_requires_one()
    {
        AnchorPayloadProblem(AnchorKind.Whole, null).ShouldBeNull();
        AnchorPayloadProblem(AnchorKind.Whole, "{}").ShouldNotBeNull();

        AnchorPayloadProblem(AnchorKind.Page, null).ShouldNotBeNull();
        AnchorPayloadProblem(AnchorKind.ModelStation, null).ShouldNotBeNull();
    }

    [Fact]
    public void Payloads_must_be_json_objects()
    {
        AnchorPayloadProblem(AnchorKind.Page, "not json").ShouldNotBeNull();
        AnchorPayloadProblem(AnchorKind.Page, "[1, 2]").ShouldNotBeNull();
        AnchorPayloadProblem(AnchorKind.Page, "42").ShouldNotBeNull();
    }

    [Fact]
    public void Unknown_extra_properties_are_tolerated()
    {
        AnchorPayloadProblem(AnchorKind.Page, """{"page": 2, "zoom": 1.5}""").ShouldBeNull();
    }

    [Theory]
    [InlineData("""{"start": 10, "end": 20, "quote": "hello"}""", true)]
    [InlineData("""{"page": 3, "start": 0, "end": 5, "quote": "q", "prefix": "", "suffix": "after"}""", true)]
    [InlineData("""{"start": 10, "end": 20}""", false)] // quote is what survives re-extraction
    [InlineData("""{"start": 10, "end": 20, "quote": "  "}""", false)]
    [InlineData("""{"start": 10, "end": 20, "quote": 7}""", false)]
    [InlineData("""{"start": -1, "end": 20, "quote": "x"}""", false)]
    [InlineData("""{"start": 10, "end": 10, "quote": "x"}""", false)] // empty selection
    [InlineData("""{"start": 10, "end": 3, "quote": "x"}""", false)] // backwards
    [InlineData("""{"start": 1.5, "end": 3, "quote": "x"}""", false)]
    [InlineData("""{"page": 0, "start": 1, "end": 3, "quote": "x"}""", false)]
    [InlineData("""{"start": 1, "end": 3, "quote": "x", "prefix": 5}""", false)]
    public void Text_ranges_need_a_quote_and_a_forward_selection(string payload, bool valid) =>
        (AnchorPayloadProblem(AnchorKind.TextRange, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData("""{"page": 1}""", true)]
    [InlineData("""{"page": 152}""", true)]
    [InlineData("""{"page": 0}""", false)] // pages are 1-based
    [InlineData("""{"page": -3}""", false)]
    [InlineData("""{"page": 2.5}""", false)]
    [InlineData("""{"page": "7"}""", false)]
    [InlineData("{}", false)]
    public void Pages_are_positive_integers(string payload, bool valid) =>
        (AnchorPayloadProblem(AnchorKind.Page, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData("""{"fromPage": 2, "toPage": 5}""", true)]
    [InlineData("""{"fromPage": 2, "toPage": 2}""", true)] // inclusive — one page is a range
    [InlineData("""{"fromPage": 5, "toPage": 2}""", false)] // backwards
    [InlineData("""{"fromPage": 0, "toPage": 2}""", false)]
    [InlineData("""{"fromPage": 2}""", false)]
    [InlineData("""{"toPage": 2}""", false)]
    public void Page_ranges_are_forward_and_positive(string payload, bool valid) =>
        (AnchorPayloadProblem(AnchorKind.PageRange, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData("""{"shape": "point", "x": 0, "y": 4.5}""", true)]
    [InlineData("""{"shape": "point", "x": -1, "y": 4}""", false)] // natural pixels
    [InlineData("""{"shape": "point", "x": 1}""", false)]
    [InlineData("""{"shape": "rect", "x": 1, "y": 1, "w": 10, "h": 5}""", true)]
    [InlineData("""{"page": 2, "shape": "rect", "x": 1, "y": 1, "w": 10, "h": 5}""", true)]
    [InlineData("""{"page": 0, "shape": "rect", "x": 1, "y": 1, "w": 10, "h": 5}""", false)]
    [InlineData("""{"shape": "rect", "x": 1, "y": 1, "w": 0, "h": 5}""", false)] // zero-size region
    [InlineData("""{"shape": "rect", "x": 1, "y": 1, "w": 10}""", false)]
    [InlineData("""{"shape": "circle", "cx": 5, "cy": 5, "r": 2}""", true)]
    [InlineData("""{"shape": "circle", "cx": 5, "cy": 5, "r": 0}""", false)]
    [InlineData("""{"shape": "polygon", "points": [[0, 0], [10, 0], [5, 8]]}""", true)]
    [InlineData("""{"shape": "polygon", "points": [[0, 0], [10, 0]]}""", false)] // < 3 points
    [InlineData("""{"shape": "polygon", "points": [[0, 0], [10, 0], [5]]}""", false)]
    [InlineData("""{"shape": "polygon", "points": [[0, 0], [10, 0], [5, "y"]]}""", false)]
    [InlineData("""{"shape": "polygon", "points": [[0, 0], [10, 0], [5, -1]]}""", false)]
    [InlineData("""{"shape": "polygon", "points": 3}""", false)]
    [InlineData("""{"shape": "polygon"}""", false)]
    [InlineData("""{"shape": "oval", "x": 1, "y": 1}""", false)]
    [InlineData("""{"x": 1, "y": 1}""", false)] // shape is required
    public void Image_regions_validate_per_shape(string payload, bool valid) =>
        (AnchorPayloadProblem(AnchorKind.ImageRegion, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData("""{"t": 0}""", true)]
    [InlineData("""{"t": 55.5}""", true)]
    [InlineData("""{"t": -0.5}""", false)] // seconds are non-negative
    [InlineData("""{"t": "55"}""", false)]
    [InlineData("{}", false)]
    public void Time_points_are_non_negative_seconds(string payload, bool valid) =>
        (AnchorPayloadProblem(AnchorKind.TimePoint, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData("""{"start": 55, "end": 80}""", true)]
    [InlineData("""{"start": 0, "end": 0.5}""", true)]
    [InlineData("""{"start": 80, "end": 55}""", false)] // backwards
    [InlineData("""{"start": 55, "end": 55}""", false)] // zero span is a time point
    [InlineData("""{"start": -1, "end": 55}""", false)]
    [InlineData("""{"start": 55}""", false)]
    public void Time_ranges_are_forward_non_negative_seconds(string payload, bool valid) =>
        (AnchorPayloadProblem(AnchorKind.TimeRange, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData(AnchorKind.ModelStation, """{"station": "pestera.intrare.p12"}""", true)]
    [InlineData(AnchorKind.ModelStation, """{"station": ""}""", false)]
    [InlineData(AnchorKind.ModelStation, """{"station": 12}""", false)]
    [InlineData(AnchorKind.ModelStation, "{}", false)]
    [InlineData(AnchorKind.ModelSurvey, """{"survey": "pestera.intrare"}""", true)]
    [InlineData(AnchorKind.ModelSurvey, "{}", false)]
    public void Stations_and_surveys_are_named(AnchorKind kind, string payload, bool valid) =>
        (AnchorPayloadProblem(kind, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData(AnchorKind.ModelStationRange, """{"fromStation": "a.p1", "toStation": "a.p9"}""", true)]
    [InlineData(AnchorKind.ModelStationRange, """{"fromStation": "a.p1"}""", false)]
    [InlineData(AnchorKind.ModelStationRange, """{"toStation": "a.p9"}""", false)]
    [InlineData(AnchorKind.ModelSurveyRange, """{"fromSurvey": "a", "toSurvey": "b"}""", true)]
    [InlineData(AnchorKind.ModelSurveyRange, """{"fromSurvey": "a"}""", false)]
    public void Station_and_survey_ranges_name_both_ends(AnchorKind kind, string payload, bool valid) =>
        (AnchorPayloadProblem(kind, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData("""{"x": 1, "y": -2.5, "z": 0}""", true)] // model coordinates may be negative
    [InlineData("""{"x": 1, "y": 2}""", false)]
    [InlineData("""{"x": 1, "y": 2, "z": "0"}""", false)]
    public void Model_points_are_three_numbers(string payload, bool valid) =>
        (AnchorPayloadProblem(AnchorKind.ModelPoint, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData("""{"index": 0}""", true)]
    [InlineData("""{"index": 3, "name": "WP3"}""", true)]
    [InlineData("""{"index": -1}""", false)]
    [InlineData("""{"index": 1.5}""", false)]
    [InlineData("""{"name": "WP3"}""", false)]
    [InlineData("""{"index": 3, "name": 5}""", false)]
    [InlineData("""{"index": 3, "name": ""}""", false)]
    public void Waypoints_are_indexed_with_an_optional_name(string payload, bool valid) =>
        (AnchorPayloadProblem(AnchorKind.Waypoint, payload) is null).ShouldBe(valid);

    [Theory]
    [InlineData("""{"fromIndex": 1, "toIndex": 4}""", true)]
    [InlineData("""{"fromIndex": 4, "toIndex": 4}""", true)]
    [InlineData("""{"fromIndex": 0, "toIndex": 2, "fromName": "A", "toName": "C"}""", true)]
    [InlineData("""{"fromIndex": 4, "toIndex": 1}""", false)] // backwards
    [InlineData("""{"fromIndex": -1, "toIndex": 4}""", false)]
    [InlineData("""{"fromIndex": 1}""", false)]
    [InlineData("""{"fromIndex": 1, "toIndex": 4, "fromName": 9}""", false)]
    public void Waypoint_ranges_are_forward_index_pairs(string payload, bool valid) =>
        (AnchorPayloadProblem(AnchorKind.WaypointRange, payload) is null).ShouldBe(valid);

    [Fact]
    public void An_unknown_anchor_kind_is_reported_as_such()
    {
        AnchorPayloadProblem((AnchorKind)99, "{}").ShouldNotBeNull();
        AnchorPayloadProblem((AnchorKind)99, "{}")!.ShouldContain("99");
    }

    // ---- member validity (composite) ----------------------------------------------

    [Fact]
    public void A_well_formed_member_has_no_problem()
    {
        MemberProblem(new(SomeId, null, null, AnchorKind.Whole, null)).ShouldBeNull();
        MemberProblem(new(null, AttachedEntityType.Document, SomeId, AnchorKind.Page, """{"page": 1}"""))
            .ShouldBeNull();
        MemberProblem(new(null, AttachedEntityType.Caver, SomeId, AnchorKind.Whole, null)).ShouldBeNull();
    }

    [Fact]
    public void Member_problems_are_reported_most_fundamental_first()
    {
        // Target shape before anything else.
        MemberProblem(new(SomeId, AttachedEntityType.Document, SomeId, AnchorKind.Whole, null))
            .ShouldBe(TargetInvalidCode);
        MemberProblem(new(null, null, null, AnchorKind.Whole, null)).ShouldBe(TargetInvalidCode);

        // Then participation.
        MemberProblem(new(null, AttachedEntityType.Comment, SomeId, AnchorKind.Whole, null))
            .ShouldBe(TypeNotLinkableCode);
        MemberProblem(new(null, AttachedEntityType.StoredFile, SomeId, AnchorKind.Whole, null))
            .ShouldBe(TypeNotLinkableCode);

        // Then the anchor-kind matrix.
        MemberProblem(new(null, AttachedEntityType.Caver, SomeId, AnchorKind.Page, """{"page": 1}"""))
            .ShouldBe(InvalidAnchorKindCode);
        MemberProblem(new(SomeId, null, null, AnchorKind.Page, """{"page": 1}"""))
            .ShouldBe(InvalidAnchorKindCode);

        // Then the payload shape.
        MemberProblem(new(null, AttachedEntityType.Document, SomeId, AnchorKind.Page, """{"page": 0}"""))
            .ShouldBe(InvalidAnchorCode);
        MemberProblem(new(null, AttachedEntityType.Document, SomeId, AnchorKind.Whole, "{}"))
            .ShouldBe(InvalidAnchorCode);
    }

    // ---- the measured-against pin ---------------------------------------------------

    [Fact]
    public void Only_a_part_anchor_into_a_document_may_carry_a_pin()
    {
        // A pin on a document part-anchor is the intended shape.
        MemberProblem(new(
                null, AttachedEntityType.Document, SomeId, AnchorKind.Page,
                """{"page": 1}""", AnchorFileId: SomeId))
            .ShouldBeNull();

        // Anything else asserts a provenance the anchor does not have: a whole-resource
        // member, a feature, a content-addressed world with no version chain.
        MemberProblem(new(SomeId, null, null, AnchorKind.Whole, null, AnchorFileId: SomeId))
            .ShouldBe(AnchorFileInvalidCode);
        MemberProblem(new(
                null, AttachedEntityType.Document, SomeId, AnchorKind.Whole, null, AnchorFileId: SomeId))
            .ShouldBe(AnchorFileInvalidCode);
        MemberProblem(new(
                null, AttachedEntityType.SurveyModel, SomeId, AnchorKind.ModelStation,
                """{"station": "pestera.p12"}""", AnchorFileId: SomeId))
            .ShouldBe(AnchorFileInvalidCode);
    }

    [Fact]
    public void An_image_region_requires_the_pin_its_pixels_are_measured_in()
    {
        RequiresAnchorFilePin(AnchorKind.ImageRegion).ShouldBeTrue();
        foreach (var kind in Enum.GetValues<AnchorKind>().Where(k => k != AnchorKind.ImageRegion))
        {
            RequiresAnchorFilePin(kind).ShouldBeFalse(kind.ToString());
        }

        const string region = """{"shape": "rect", "x": 10, "y": 10, "w": 5, "h": 5}""";
        MemberProblem(new(
                null, AttachedEntityType.Document, SomeId, AnchorKind.ImageRegion, region))
            .ShouldBe(AnchorPinRequiredCode);
        MemberProblem(new(
                null, AttachedEntityType.Document, SomeId, AnchorKind.ImageRegion, region,
                AnchorFileId: SomeId))
            .ShouldBeNull();
    }

    // ---- the member ceiling ---------------------------------------------------------

    [Fact]
    public void A_link_holds_a_bounded_number_of_members()
    {
        MayAddMember(0).ShouldBeTrue();
        MayAddMember(MaxMembers - 1).ShouldBeTrue();
        MayAddMember(MaxMembers).ShouldBeFalse();
    }

    // ---- main marker --------------------------------------------------------------

    [Fact]
    public void Undirected_relations_forbid_a_main_member()
    {
        MainMarkerProblem(directed: false, memberCount: 2, mainCount: 0).ShouldBeNull();
        MainMarkerProblem(directed: false, memberCount: 2, mainCount: 1).ShouldBe(MainNotAllowedCode);
    }

    [Fact]
    public void Directed_relations_require_exactly_one_main_from_two_members_up()
    {
        MainMarkerProblem(directed: true, memberCount: 2, mainCount: 0).ShouldBe(MainRequiredCode);
        MainMarkerProblem(directed: true, memberCount: 2, mainCount: 1).ShouldBeNull();
        MainMarkerProblem(directed: true, memberCount: 3, mainCount: 2).ShouldBe(MainNotSingleCode);
    }

    [Fact]
    public void A_one_member_link_has_nothing_to_read_towards()
    {
        MainMarkerProblem(directed: true, memberCount: 1, mainCount: 0).ShouldBeNull();
        MainMarkerProblem(directed: true, memberCount: 1, mainCount: 1).ShouldBe(MainNotAllowedCode);
    }

    [Fact]
    public void An_untyped_link_is_undirected()
    {
        MainMarkerProblem(relationType: null, memberCount: 2, mainCount: 0).ShouldBeNull();
        MainMarkerProblem(relationType: null, memberCount: 2, mainCount: 1).ShouldBe(MainNotAllowedCode);

        var contains = new ResLinkRelationType { Code = "contains", Name = "Contains", Directed = true };
        MainMarkerProblem(contains, memberCount: 2, mainCount: 0).ShouldBe(MainRequiredCode);
        MainMarkerProblem(contains, memberCount: 2, mainCount: 1).ShouldBeNull();

        var related = new ResLinkRelationType { Code = "related-to", Name = "Related to" };
        MainMarkerProblem(related, memberCount: 2, mainCount: 1).ShouldBe(MainNotAllowedCode);
    }

    // ---- audience of a point minted alongside a member ------------------------------

    [Fact]
    public void A_new_point_takes_the_one_group_its_creator_belongs_to()
    {
        var group = Guid.CreateVersion7();
        DefaultPointAudience([group]).ShouldBe((Visibility.CavingGroup, group));
    }

    [Fact]
    public void A_new_point_widens_to_every_signed_in_reader_with_no_single_group_to_mean()
    {
        // No membership, and several memberships, land in the same place: a group-visible
        // row that names no group would admit nobody, and no membership outranks another.
        DefaultPointAudience([]).ShouldBe((Visibility.Authenticated, (Guid?)null));
        DefaultPointAudience([Guid.CreateVersion7(), Guid.CreateVersion7()])
            .ShouldBe((Visibility.Authenticated, (Guid?)null));
    }

    [Fact]
    public void A_new_point_is_never_public_and_never_private_by_default()
    {
        foreach (var groups in new[] { Array.Empty<Guid>(), [Guid.CreateVersion7()], new[] { Guid.CreateVersion7(), Guid.CreateVersion7() } })
        {
            var (visibility, _) = DefaultPointAudience(groups);
            visibility.ShouldNotBe(Visibility.Public);
            visibility.ShouldNotBe(Visibility.Private);
        }
    }

    // ---- membership floor ---------------------------------------------------------

    [Fact]
    public void The_last_member_is_not_removable()
    {
        MinMembers.ShouldBe(1);
        MayRemoveMember(2).ShouldBeTrue();
        MayRemoveMember(1).ShouldBeFalse();
    }

    // ---- short code ---------------------------------------------------------------

    [Fact]
    public void Short_codes_are_eight_base62_characters()
    {
        ShortCodeAlphabet.Length.ShouldBe(62);
        ShortCodeAlphabet.Distinct().Count().ShouldBe(62);

        IsShortCode("ABCDefgh").ShouldBeTrue();
        IsShortCode("12345678").ShouldBeTrue();

        IsShortCode(null).ShouldBeFalse();
        IsShortCode("").ShouldBeFalse();
        IsShortCode("abc").ShouldBeFalse();
        IsShortCode("abcdefghi").ShouldBeFalse(); // 9 chars
        IsShortCode("abcd-123").ShouldBeFalse(); // outside the alphabet
        IsShortCode("abcdefgé").ShouldBeFalse(); // non-ASCII letter
        IsShortCode(Guid.CreateVersion7().ToString()).ShouldBeFalse(); // ids stay distinguishable
    }

    [Fact]
    public void New_short_codes_are_valid_and_do_not_repeat_in_practice()
    {
        var codes = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var code = NewShortCode();
            IsShortCode(code).ShouldBeTrue();
            code.ShouldAllBe(c => ShortCodeAlphabet.Contains(c));
            codes.Add(code);
        }

        // 62^8 values — 200 draws colliding would point at a broken generator.
        codes.Count.ShouldBe(200);
    }

    // ---- membership disclosure ------------------------------------------------------

    [Fact]
    public void A_membership_naming_a_feature_answers_the_association_rule()
    {
        var membership = MemberAssociation(SomeId);

        // Withheld exactly when the feature's coordinates would be: the caller lacks
        // exact view and the installation keeps its default.
        AssociationProtection.IsWithheld(
                membership, exactViewOfTarget: false, revealProtectedAssociations: false)
            .ShouldBeTrue();

        // …and the two disclosures that must survive the withholding: exact view on the
        // feature, or the installation deciding to reveal associations.
        AssociationProtection.IsWithheld(
                membership, exactViewOfTarget: true, revealProtectedAssociations: false)
            .ShouldBeFalse();
        AssociationProtection.IsWithheld(
                membership, exactViewOfTarget: false, revealProtectedAssociations: true)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_membership_naming_no_feature_is_never_withheld()
    {
        // An entity-world member — a document, a trip, a caver — names no feature for the
        // rule to guard; what travels for it is its own world's business. A trip carries a
        // sketch of its own, and that still does not withhold the trip's own row: it counts
        // only for the members standing beside it, through the sibling mapping.
        MemberAssociation(null).TargetFeatureId.ShouldBeNull();
        AssociationProtection.IsWithheld(
                MemberAssociation(null), exactViewOfTarget: false, revealProtectedAssociations: false)
            .ShouldBeFalse();
    }

    [Fact]
    public void A_membership_row_carries_no_position_of_its_own()
    {
        // Unlike a geotagged photo, nothing in a bare membership row is coordinates —
        // so the sibling-less mapping can never trip the rule's always-withheld arm,
        // and the setting genuinely decides.
        MemberAssociation(SomeId).DocumentCarriesItsOwnPosition.ShouldBeFalse();
    }

    [Fact]
    public void A_sibling_showing_coordinates_withholds_past_the_reveal_setting()
    {
        // A link whose other member shows the caller exact coordinates puts the
        // protected feature's name beside a position — the same pairing as a geotagged
        // photo attached to it, mapped onto the same arm of the same rule, which the
        // installation's reveal setting never opens.
        var beside = MemberAssociation(SomeId, siblingShowsCoordinates: true);
        AssociationProtection.IsWithheld(
                beside, exactViewOfTarget: false, revealProtectedAssociations: true)
            .ShouldBeTrue();
        AssociationProtection.IsWithheld(
                beside, exactViewOfTarget: false, revealProtectedAssociations: false)
            .ShouldBeTrue();

        // The matching positives: exact view on the feature ends the question — there
        // is no position left to protect from its own coordinates — and without the
        // exposing sibling the reveal setting decides as before.
        AssociationProtection.IsWithheld(
                beside, exactViewOfTarget: true, revealProtectedAssociations: false)
            .ShouldBeFalse();
        AssociationProtection.IsWithheld(
                MemberAssociation(SomeId, siblingShowsCoordinates: false),
                exactViewOfTarget: false, revealProtectedAssociations: true)
            .ShouldBeFalse();

        // An entity-world member has no position for the sibling to give away.
        AssociationProtection.IsWithheld(
                MemberAssociation(null, siblingShowsCoordinates: true),
                exactViewOfTarget: false, revealProtectedAssociations: false)
            .ShouldBeFalse();
    }

    [Fact]
    public void Waypoint_anchors_are_the_kinds_that_address_coordinates()
    {
        // A waypoint anchor names positioned points of a geofile, so a member carrying
        // one shows its reader coordinates; every other kind addresses content.
        AnchorAddressesCoordinates(AnchorKind.Waypoint).ShouldBeTrue();
        AnchorAddressesCoordinates(AnchorKind.WaypointRange).ShouldBeTrue();
        foreach (var kind in Enum.GetValues<AnchorKind>()
                     .Where(k => k is not (AnchorKind.Waypoint or AnchorKind.WaypointRange)))
        {
            AnchorAddressesCoordinates(kind).ShouldBeFalse(kind.ToString());
        }
    }

    // ---- anchor presentation --------------------------------------------------------

    [Fact]
    public void An_unreadable_target_takes_its_whole_anchor_with_it()
    {
        // Payload (it can quote the content), pin (it names a file of the document) and
        // degradation (re-versioning is a fact about the document) all stay withheld,
        // whatever the pin's state is.
        foreach (var superseded in new[] { false, true })
        {
            foreach (var editor in new[] { false, true })
            {
                PresentAnchor(
                        targetReadable: false,
                        pinSuperseded: superseded,
                        supersededVersionsReadable: editor)
                    .ShouldBe(new AnchorPresentation(false, false, false));
            }
        }
    }

    [Fact]
    public void A_current_pin_travels_whole_to_any_reader()
    {
        PresentAnchor(targetReadable: true, pinSuperseded: false, supersededVersionsReadable: false)
            .ShouldBe(new AnchorPresentation(
                PayloadShown: true, PinShown: true, DegradationShown: false));
    }

    [Fact]
    public void A_superseded_pin_degrades_for_all_readers_but_routes_only_editors()
    {
        // Every reader is told the anchor no longer addresses what the document serves —
        // that is a fact of this link. Only a caller whom the document's own rules let
        // into superseded versions gets the superseded file's id; a read-only caller
        // must never be handed a marker into history their document read would refuse.
        PresentAnchor(targetReadable: true, pinSuperseded: true, supersededVersionsReadable: true)
            .ShouldBe(new AnchorPresentation(
                PayloadShown: true, PinShown: true, DegradationShown: true));
        PresentAnchor(targetReadable: true, pinSuperseded: true, supersededVersionsReadable: false)
            .ShouldBe(new AnchorPresentation(
                PayloadShown: true, PinShown: false, DegradationShown: true));
    }
}
