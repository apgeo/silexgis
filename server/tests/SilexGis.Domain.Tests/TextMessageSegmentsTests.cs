// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Messaging;

namespace SilexGis.Domain.Tests;

/// <summary>
/// A table of messages against the two alphabets, because the interesting cases are all
/// boundaries: the character where a message stops fitting, and the character where it stops
/// being narrow. The second of those is the one that hides — it flips on one letter with no
/// change in length, so a count taken over English wording agrees with a count taken over
/// characters and proves nothing, while the real bill is twice what it says.
/// </summary>
public class TextMessageSegmentsTests
{
    /// <summary>
    /// One announcement as it is actually handed over, twice: once with a member's surname
    /// spelled without the mark somebody would type in a hurry, and once with it. The two
    /// differ by a single character and nothing else, including their length.
    /// </summary>
    private const string PlainName =
        "SilexGIS: Andrei Padurean wrote to Clubul Speologic Cluj. Read it at https://silex.example.org/a/7f3c";

    private const string OneDiacritic =
        "SilexGIS: Andrei Pădurean wrote to Clubul Speologic Cluj. Read it at https://silex.example.org/a/7f3c";

    [Fact]
    public void A_message_with_nothing_in_it_is_one_segment_rather_than_none()
    {
        // A gateway handed an empty text takes it and charges for it, so a guard that answered
        // nothing here would spend a wording that rendered empty for free.
        TextMessageSegments.Count(string.Empty).ShouldBe(1);
        TextMessageSegments.Count(null).ShouldBe(1);
    }

    [Fact]
    public void A_plain_announcement_travels_in_one_segment()
    {
        PlainName.Length.ShouldBe(101);

        TextMessageSegments.Count(PlainName).ShouldBe(1);
    }

    [Fact]
    public void The_same_announcement_with_one_diacritic_in_a_name_travels_in_two()
    {
        // The whole defect in one line. Same wording, same language, same 101 characters — one
        // of them carries a breve, so the message is re-encoded wide, where 70 is a segment.
        // Nothing about the text got longer and the bill doubled.
        OneDiacritic.Length.ShouldBe(PlainName.Length);

        TextMessageSegments.Count(OneDiacritic).ShouldBe(2);
    }

    /// <summary>
    /// The one wording this installation can send in bulk, weighed with an ordinary club and an
    /// ordinary member in it.
    /// </summary>
    /// <remarks>
    /// Here so that the floor a message is charged before it has been rendered cannot be mistaken
    /// for a bound on what one costs. The message carries a club's name and a person's, and names
    /// are as long as they are: this one runs to three pieces in the language the wording itself
    /// needs the wide alphabet for, and to two in the other. A guard that charged the floor for
    /// work it had not yet rendered would therefore accept an announcement half again as
    /// expensive as it projected, which is why what is known before sending is weighed instead.
    /// </remarks>
    [Fact]
    public void The_announcement_wording_costs_more_than_the_floor_once_a_club_and_a_caver_are_named()
    {
        var wording = MessageTemplateCatalog
            .On(MessageTemplateCatalog.NotifyGroupAnnouncement, MessageChannel.Sms)
            .ShouldNotBeNull();

        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["appName"] = "SilexGIS",
            ["actorName"] = "Andrei Pădurean",
            ["cavingGroupName"] = "Clubul de Speologie Emil Racoviță Cluj-Napoca",
            ["url"] = "https://gis.example.org/notifications",
        };

        Pieces(wording, "ro", values).ShouldBe(3);
        Pieces(wording, "ro", values).ShouldBeGreaterThan(TextMessageSegments.Unrendered);

        // The same message in the other language, which is shorter but no narrower: the names in
        // it carry marks of their own, so this one is two pieces rather than one. Nothing here is
        // a claim that English is free — only that the wording that costs most is the one to
        // measure a ceiling with.
        Pieces(wording, "en", values).ShouldBe(2);
    }

    private static int Pieces(
        MessageTemplateDefinition wording, string locale, IReadOnlyDictionary<string, string> values) =>
        TextMessageSegments.Count(
            MessageTemplateRenderer.Tidy(
                MessageTemplateRenderer.Render(
                    MessageTemplateCatalog.Default(wording, locale).Body, values)));

    [Fact]
    public void Exactly_a_full_narrow_segment_is_one_and_the_next_character_is_two()
    {
        TextMessageSegments.Count(Plain(160)).ShouldBe(1);
        TextMessageSegments.Count(Plain(161)).ShouldBe(2);
    }

    [Fact]
    public void Exactly_a_full_wide_segment_is_one_and_the_next_character_is_two()
    {
        TextMessageSegments.Count(Wide(70)).ShouldBe(1);
        TextMessageSegments.Count(Wide(71)).ShouldBe(2);
    }

    [Fact]
    public void A_message_that_runs_to_several_segments_fits_less_in_each_of_them()
    {
        // Every segment of a split message spends room on the header saying which part of what
        // it is, the first one included — so the second segment starts at 154 and not at 161,
        // and three segments begin at 307 rather than at 321.
        TextMessageSegments.Count(Plain(306)).ShouldBe(2);
        TextMessageSegments.Count(Plain(307)).ShouldBe(3);

        TextMessageSegments.Count(Wide(134)).ShouldBe(2);
        TextMessageSegments.Count(Wide(135)).ShouldBe(3);
    }

    [Fact]
    public void A_character_the_narrow_alphabet_reaches_only_by_escaping_spends_two()
    {
        // 159 letters and a euro sign is 161 characters' worth of room, not 160.
        TextMessageSegments.Count(Plain(158) + "€").ShouldBe(1);
        TextMessageSegments.Count(Plain(159) + "€").ShouldBe(2);
    }

    [Fact]
    public void An_escaped_character_is_not_broken_across_the_join_between_two_segments()
    {
        // 306 characters' worth of room is exactly two segments of 153 — but here the escaped
        // character starts one unit before the join and may not be split, so it moves whole to
        // the second segment and pushes one letter into a third. Dividing the length would say
        // two, and the carrier bills three.
        var straddling = Plain(152) + "€" + Plain(152);

        TextMessageSegments.Count(straddling).ShouldBe(3);
        TextMessageSegments.Count(Plain(306)).ShouldBe(2);
    }

    [Fact]
    public void A_character_beyond_the_basic_plane_spends_two_of_a_wide_segment()
    {
        // Written as a pair of code units, and never split across the join for the same reason.
        TextMessageSegments.Count(Wide(68) + "😀").ShouldBe(1);
        TextMessageSegments.Count(Wide(69) + "😀").ShouldBe(2);
    }

    [Theory]
    [InlineData('à')]
    [InlineData('ä')]
    [InlineData('é')]
    [InlineData('è')]
    [InlineData('ñ')]
    [InlineData('ö')]
    [InlineData('ü')]
    [InlineData('ß')]
    [InlineData('Ø')]
    [InlineData('§')]
    [InlineData('¿')]
    [InlineData('\n')]
    public void A_letter_the_narrow_alphabet_carries_leaves_the_message_narrow(char letter) =>
        // 101 characters: one segment while the message is narrow, two the moment it is not.
        TextMessageSegments.Count(Plain(100) + letter).ShouldBe(1);

    [Theory]
    [InlineData('ă')]
    [InlineData('â')]
    [InlineData('î')]
    [InlineData('ș')]
    [InlineData('ț')]
    [InlineData('Ă')]
    [InlineData('Î')]
    [InlineData('Ș')]
    [InlineData('Ț')]
    [InlineData('ş')]
    [InlineData('ţ')]
    [InlineData('\t')]
    public void A_letter_it_does_not_carry_halves_what_fits(char letter) =>
        // Every Romanian diacritic is outside the narrow alphabet, in both the comma-below
        // spelling and the cedilla one older keyboards still produce — and so is the tab, which
        // looks harmless and is not in it either.
        TextMessageSegments.Count(Plain(100) + letter).ShouldBe(2);

    [Theory]
    [InlineData('^')]
    [InlineData('{')]
    [InlineData('}')]
    [InlineData('\\')]
    [InlineData('[')]
    [InlineData('~')]
    [InlineData(']')]
    [InlineData('|')]
    [InlineData('€')]
    [InlineData('\f')]
    public void An_escaped_character_costs_two_where_a_letter_costs_one(char escaped) =>
        // The same 160 positions filled with 159 letters and one of these is 161 units' worth,
        // so it no longer fits where 160 letters do.
        TextMessageSegments.Count(Plain(159) + escaped).ShouldBe(2);

    /// <summary>Characters the narrow alphabet carries one for one.</summary>
    private static string Plain(int count) => new('a', count);

    /// <summary>Characters that force the wide alphabet, one code unit each.</summary>
    private static string Wide(int count) => new('ă', count);
}
