// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Surveys;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Converting between the name the survey rows hold for a station and the name the viewer that
/// draws the model addresses the same station by.
///
/// <para>
/// Every case here is driven from one two-level survey written out in both spellings, because the
/// defect this rule exists to stop was two derivations of the same path drifting apart, and a test
/// that states only one side of it would drift with them.
/// </para>
/// </summary>
public class SurveyStationNamesTests
{
    /// <summary>
    /// The model with a survey tree, root included. Written as the file's own structure rather than
    /// as strings: root <c>cave</c>, sub-survey <c>entrance</c>, sub-sub-survey <c>pit</c>, with a
    /// station in each.
    /// </summary>
    private const string Root = "cave";

    /// <summary>
    /// A station sitting directly in the root survey, one a level down, one two levels down, and
    /// what the viewer calls each of them. The viewer's reader leaves the root survey out of its
    /// tree, so the names differ by exactly that one leading component — never by more, and never
    /// at the far end.
    /// </summary>
    public static TheoryData<string, string> TwoLevelTree => new()
    {
        { "cave.0", "0" },
        { "cave.entrance.1", "entrance.1" },
        { "cave.entrance.pit.2", "entrance.pit.2" },
    };

    [Theory]
    [MemberData(nameof(TwoLevelTree))]
    public void A_therion_model_drops_the_root_survey_the_viewer_never_added(string stored, string viewer)
    {
        SurveyStationNames.ViewerName(SurveyModelFormat.Lox, Root, stored).ShouldBe(viewer);
    }

    [Theory]
    [MemberData(nameof(TwoLevelTree))]
    public void And_puts_it_back_to_find_the_row_again(string stored, string viewer)
    {
        // The positive twin of the rule above: the conversion is a prefix, so reading it backwards
        // has to land on the row it came from. A rule that only ever cut would let a name pressed
        // on the model resolve to nothing, which is the shape the defect took.
        SurveyStationNames.StoredName(SurveyModelFormat.Lox, Root, viewer).ShouldBe(stored);
    }

    [Theory]
    [MemberData(nameof(TwoLevelTree))]
    public void A_survex_model_spells_a_station_one_way_and_it_is_the_same_way(string stored, string viewer)
    {
        // The same tree in the format that carries no survey tree at all: each label already holds
        // its whole path, both sides take it as it is, and converting must do nothing. That the
        // strings above differ is the point — the .3d answer is the stored name, not the viewer
        // name of the .lox case.
        SurveyStationNames.ViewerName(SurveyModelFormat.Survex3d, Root, stored).ShouldBe(stored);
        SurveyStationNames.StoredName(SurveyModelFormat.Survex3d, Root, stored).ShouldBe(stored);
        viewer.ShouldNotBe(stored);
    }

    [Theory]
    // A file whose root survey has no name of its own: the path built from the rows starts at the
    // first named survey, the viewer's tree starts at the same place, and there is nothing to drop.
    // This is the case a rule that cut at the first separator would break — it would answer "1" for
    // a station the viewer calls "a.1", and the marker would land nowhere.
    [InlineData("a.1")]
    [InlineData("1")]
    public void A_model_whose_root_is_unnamed_already_agrees_with_the_viewer(string stored)
    {
        SurveyStationNames.ViewerName(SurveyModelFormat.Lox, null, stored).ShouldBe(stored);
        SurveyStationNames.ViewerName(SurveyModelFormat.Lox, "", stored).ShouldBe(stored);
        SurveyStationNames.StoredName(SurveyModelFormat.Lox, null, stored).ShouldBe(stored);
    }

    [Fact]
    public void Only_the_one_format_with_a_survey_tree_converts_anything()
    {
        // Every other format's names are whole as they stand, root survey name or not — a mesh has
        // no survey tree to have a root of, and neither does a file whose labels already carry
        // their whole path. Stated as one list so a format added later has somewhere obvious to be
        // answered, and so that "converts nothing" is checked rather than assumed of the default.
        foreach (var format in new[] { SurveyModelFormat.Survex3d, SurveyModelFormat.Stl })
        {
            SurveyStationNames.ViewerName(format, Root, "cave.entrance.1").ShouldBe("cave.entrance.1");
            SurveyStationNames.StoredName(format, Root, "cave.entrance.1").ShouldBe("cave.entrance.1");
            SurveyStationNames.StoredCandidates(format, Root, "cave.entrance.1").ShouldBe(["cave.entrance.1"]);
        }

        // And the format that does have one converts only where its root is named — the shape every
        // file of it seen so far actually has is the unnamed one, which converts nothing either.
        SurveyStationNames.ViewerName(SurveyModelFormat.Lox, Root, "cave.entrance.1").ShouldBe("entrance.1");
        SurveyStationNames.ViewerName(SurveyModelFormat.Lox, null, "cave.entrance.1").ShouldBe("cave.entrance.1");
        SurveyStationNames.ViewerName(SurveyModelFormat.Lox, "", "cave.entrance.1").ShouldBe("cave.entrance.1");
    }

    [Theory]
    // A station of some other survey that merely begins with the same letters. Nothing is cut,
    // because the prefix is the root's name *and its separator* — "cavern.1" is a name in its own
    // right and shortening it to "n.1" would name nothing.
    [InlineData("cavern.1")]
    // A name the file gave no survey path at all, which both sides then call by its bare name.
    [InlineData("7")]
    public void A_name_that_does_not_carry_the_root_is_left_exactly_as_it_is(string stored)
    {
        SurveyStationNames.ViewerName(SurveyModelFormat.Lox, Root, stored).ShouldBe(stored);
    }

    [Fact]
    public void The_root_survey_name_alone_names_no_station_and_is_not_cut_to_nothing()
    {
        // Cutting here would answer with the empty string — a name no viewer resolves and no two
        // rows could be told apart by. The row is left as it is and whoever asked is told, by the
        // lookup that follows, that the model has no such station.
        SurveyStationNames.ViewerName(SurveyModelFormat.Lox, Root, Root).ShouldBe(Root);
    }

    [Fact]
    public void A_name_is_looked_for_under_both_readings_with_the_viewers_first()
    {
        // The order is the rule: a name arriving from the model is tried as the model meant it
        // before it is tried as a row. Both are offered, because either side of the application
        // can be the one holding the name.
        SurveyStationNames.StoredCandidates(SurveyModelFormat.Lox, Root, "entrance.1")
            .ShouldBe(["cave.entrance.1", "entrance.1"]);

        // Where the two spellings agree there is one reading and it is offered once — a second,
        // identical candidate would only make a caller look twice for the same row.
        SurveyStationNames.StoredCandidates(SurveyModelFormat.Lox, null, "entrance.1")
            .ShouldBe(["entrance.1"]);
        SurveyStationNames.StoredCandidates(SurveyModelFormat.Survex3d, Root, "entrance.1")
            .ShouldBe(["entrance.1"]);
    }

    [Fact]
    public void A_match_answers_in_the_viewers_words_whichever_words_it_arrived_in()
    {
        // Whatever was typed, what comes back is what the viewer will be asked to draw. The model
        // here holds one station, spelled as the rows spell it.
        bool Holds(string name) => name == "cave.entrance.1";

        SurveyStationNames.ViewerNameOfMatch(SurveyModelFormat.Lox, Root, "entrance.1", Holds)
            .ShouldBe("entrance.1");
        SurveyStationNames.ViewerNameOfMatch(SurveyModelFormat.Lox, Root, "cave.entrance.1", Holds)
            .ShouldBe("entrance.1");

        // The positive twin of accepting two readings: a name that is neither reading of a station
        // this model holds is still refused, and refused as null rather than guessed at.
        SurveyStationNames.ViewerNameOfMatch(SurveyModelFormat.Lox, Root, "cave.cave.entrance.1", Holds)
            .ShouldBeNull();
        SurveyStationNames.ViewerNameOfMatch(SurveyModelFormat.Lox, Root, "entrance.2", Holds).ShouldBeNull();
    }

    [Fact]
    public void A_station_named_like_its_own_root_survey_keeps_both_readings_available()
    {
        // The one case where the two spellings can collide inside one model: a sub-survey called
        // exactly what the root is called. Converting is still a prefix and still reversible; which
        // of the two rows a caller meant is decided where the rows are, by trying the viewer's
        // reading first, and not here.
        SurveyStationNames.ViewerName(SurveyModelFormat.Lox, Root, "cave.cave.3").ShouldBe("cave.3");
        SurveyStationNames.StoredName(SurveyModelFormat.Lox, Root, "cave.3").ShouldBe("cave.cave.3");
    }

    [Theory]
    [InlineData("-")]
    [InlineData(".")]
    public void The_survey_languages_two_ways_of_saying_there_is_no_station_here_are_known_as_such(
        string placeholder)
    {
        // Both, and not only the first. A rule that knew the dash alone would still refuse most
        // real surveys: of thirteen measured files that could not be read at all, five carried no
        // dash whatsoever and were full of the full stop instead, and five more carried both.
        SurveyStationNames.IsAnonymousPoint(placeholder).ShouldBeTrue();
    }

    [Theory]
    [InlineData("A")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData(".1")]
    [InlineData("1.0")]
    [InlineData("--")]
    [InlineData("..")]
    [InlineData("-.")]
    [InlineData("BH_Surface.1.0")]
    [InlineData("")]
    [InlineData(null)]
    public void A_station_somebody_actually_named_is_not_one_of_them(string? realName)
    {
        // The positive twin, and the reason the comparison is against the whole name rather than a
        // first character. Real station names beginning with a full stop or a dash exist — one
        // measured survey names thousands of stations in the shape "1.0" — and a leading-punctuation
        // rule would quietly stop storing every one of them while looking like it had fixed
        // something. Doubling the character is a name too: the placeholder is one character, whole.
        SurveyStationNames.IsAnonymousPoint(realName).ShouldBeFalse();
    }

    [Fact]
    public void The_placeholder_is_the_leaf_name_and_never_the_qualified_one()
    {
        // What a placeholder becomes once the survey path is in front of it — which is exactly the
        // string a whole survey's wall shots used to arrive at the station table under, one row per
        // wall shot, all spelled the same. Asked of that string the answer is no, because by then
        // it is no longer the file's token: the question belongs at the leaf, before qualifying,
        // and stating it here is what stops the check drifting to the wrong end of the name.
        SurveyStationNames.IsAnonymousPoint("cave.entrance.-").ShouldBeFalse();
        SurveyStationNames.IsAnonymousPoint("cave.entrance.").ShouldBeFalse();
    }
}
