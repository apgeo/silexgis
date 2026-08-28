// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Messaging;
using SilexGis.Domain.Notifications;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Rendering and validating the message wording an operator can rewrite.
/// </summary>
public class MessageTemplateTests
{
    [Fact]
    public void Placeholders_are_replaced_by_their_values()
    {
        var rendered = MessageTemplateRenderer.Render(
            "Your {appName} code is {code}.",
            new Dictionary<string, string> { ["appName"] = "SilexGIS", ["code"] = "123456" });

        rendered.ShouldBe("Your SilexGIS code is 123456.");
    }

    [Fact]
    public void An_unsupplied_placeholder_renders_as_nothing_rather_than_throwing()
    {
        // A template edited into a broken state must still let someone reset their password.
        MessageTemplateRenderer.Render("Code: {code}.", new Dictionary<string, string>())
            .ShouldBe("Code: .");
    }

    [Fact]
    public void Prose_containing_a_brace_survives_untouched()
    {
        MessageTemplateRenderer.Render("Use {to} and { spaced } and {}", new Dictionary<string, string> { ["to"] = "x" })
            .ShouldBe("Use x and { spaced } and {}");
    }

    [Fact]
    public void Placeholders_are_reported_once_each_in_the_order_they_appear()
    {
        MessageTemplateRenderer.PlaceholdersIn("{b} {a} {b} {c}")
            .ShouldBe(["b", "a", "c"]);
    }

    [Fact]
    public void A_placeholder_the_message_is_never_given_is_rejected()
    {
        var definition = MessageTemplateCatalog.Find(MessageTemplateCatalog.SmsTwoFactorCode)!;

        MessageTemplateRenderer.UnknownPlaceholders(definition, null, "Your code is {code} for {caveName}.")
            .ShouldBe(["caveName"]);
    }

    [Fact]
    public void A_template_using_only_declared_placeholders_is_accepted()
    {
        var definition = MessageTemplateCatalog.Find(MessageTemplateCatalog.EmailTwoFactorCode)!;

        MessageTemplateRenderer
            .UnknownPlaceholders(definition, "{appName} code", "Hello {displayName}, use {code} within {expiresMinutes}m.")
            .ShouldBeEmpty();
    }

    [Fact]
    public void Every_shipped_template_only_uses_placeholders_it_declares()
    {
        // Guards the defaults themselves against the same mistake the editor refuses: a shipped
        // message with a placeholder nothing fills would arrive with a hole in it.
        foreach (var definition in MessageTemplateCatalog.All)
        {
            foreach (var locale in MessageTemplateCatalog.Locales)
            {
                var text = MessageTemplateCatalog.Default(definition, locale);
                MessageTemplateRenderer.UnknownPlaceholders(definition, text.Subject, text.Body)
                    .ShouldBeEmpty($"{definition.Key} ({locale})");
            }
        }
    }

    [Theory]
    [InlineData(MessageTemplateCatalog.NotifyTripPlanInvitation)]
    [InlineData(MessageTemplateCatalog.NotifyTripPlanChanged)]
    [InlineData(MessageTemplateCatalog.NotifyTripPlanCancelled)]
    public void A_message_about_a_planned_trip_may_say_only_which_trip_and_when(string key)
    {
        // The places a trip is about are readable by fewer people than the people it is about,
        // so none of these may carry one. Pinning the declared list rather than scanning the
        // wording is what makes that hold: the renderer refuses any placeholder off the list, so
        // a cave can only enter the wording by being declared here first.
        var definition = MessageTemplateCatalog.Find(key)!;

        definition.Channel.ShouldBe(MessageChannel.Email);
        definition.Placeholders.ShouldBe(
            ["appName", "displayName", "actorName", "tripTitle", "tripDate", "url", "unsubscribeUrl"],
            ignoreOrder: true);

        foreach (var locale in MessageTemplateCatalog.Locales)
        {
            definition.Defaults.ShouldContainKey(locale, $"{key} is missing {locale}");

            var text = MessageTemplateCatalog.Default(definition, locale);
            text.Subject.ShouldNotBeNullOrWhiteSpace($"{key} ({locale})");
            MessageTemplateRenderer.UnknownPlaceholders(definition, text.Subject, text.Body)
                .ShouldBeEmpty($"{key} ({locale})");
        }
    }

    [Fact]
    public void No_shipped_template_writes_the_installation_address_in_front_of_a_link()
    {
        // The sender resolves a message's own "url" against the installation's address before
        // rendering, so a wording that writes the two side by side prints the address twice and
        // produces a link no mail client can open. Declaring both is what makes that wording
        // possible at all, so both halves are pinned: neither may a shipped body write the pair,
        // nor may a definition declare "siteUrl" alongside "url" and let an operator rewrite
        // reintroduce it.
        foreach (var definition in MessageTemplateCatalog.All)
        {
            if (definition.Placeholders.Contains("url", StringComparer.Ordinal))
            {
                definition.Placeholders.ShouldNotContain("siteUrl", definition.Key);
            }

            foreach (var locale in MessageTemplateCatalog.Locales)
            {
                var text = MessageTemplateCatalog.Default(definition, locale);
                var whole = (text.Subject ?? string.Empty) + "\n" + text.Body;
                whole.Contains("{siteUrl}{url}", StringComparison.Ordinal)
                    .ShouldBeFalse($"{definition.Key} ({locale})");
            }
        }
    }

    [Fact]
    public void A_message_a_scheduled_pass_sends_names_no_actor_and_no_cave()
    {
        // Neither of these is caused by anybody, so neither declares an actor — a placeholder
        // nothing fills is one an operator can write into the wording and get a hole from. And
        // neither may name a cave. The temptation is at its worst on the overdue one, where an
        // alarm feels like the message that ought to say where the party is; but it goes to
        // everybody the trip names, and where a cave is stays readable by fewer people than that.
        var reminder = MessageTemplateCatalog.Find(MessageTemplateCatalog.NotifyTripPlanReminder)!;

        reminder.Placeholders.ShouldBe(
            ["appName", "displayName", "tripTitle", "tripDate", "url", "unsubscribeUrl"],
            ignoreOrder: true);

        var overdue = MessageTemplateCatalog.Find(MessageTemplateCatalog.NotifyTripCalloutOverdue)!;

        // The overdue one carries no opt-out line, the way the account-security messages do not:
        // nobody may switch a callout off, so a link that could not work would be a lie. Leaving
        // the placeholder undeclared is what stops an operator putting one back.
        overdue.Placeholders.ShouldBe(
            ["appName", "displayName", "tripTitle", "tripDate", "expectedReturn", "url"],
            ignoreOrder: true);

        foreach (var definition in new[] { reminder, overdue })
        {
            definition.Channel.ShouldBe(MessageChannel.Email);

            foreach (var locale in MessageTemplateCatalog.Locales)
            {
                definition.Defaults.ShouldContainKey(locale, $"{definition.Key} is missing {locale}");

                var text = MessageTemplateCatalog.Default(definition, locale);
                text.Subject.ShouldNotBeNullOrWhiteSpace($"{definition.Key} ({locale})");
                MessageTemplateRenderer.UnknownPlaceholders(definition, text.Subject, text.Body)
                    .ShouldBeEmpty($"{definition.Key} ({locale})");
            }
        }
    }

    [Fact]
    public void Every_shipped_template_exists_in_every_language()
    {
        foreach (var definition in MessageTemplateCatalog.All)
        {
            foreach (var locale in MessageTemplateCatalog.Locales)
            {
                definition.Defaults.ShouldContainKey(locale, $"{definition.Key} is missing {locale}");
            }
        }
    }

    [Fact]
    public void Email_templates_have_a_subject_and_sms_templates_do_not()
    {
        foreach (var definition in MessageTemplateCatalog.All)
        {
            foreach (var locale in MessageTemplateCatalog.Locales)
            {
                var text = MessageTemplateCatalog.Default(definition, locale);
                if (definition.Channel == MessageChannel.Email)
                {
                    text.Subject.ShouldNotBeNullOrWhiteSpace(definition.Key);
                }
                else
                {
                    text.Subject.ShouldBeNull(definition.Key);
                }
            }
        }
    }

    [Fact]
    public void A_message_written_twice_names_a_second_wording_that_exists_and_travels_another_way()
    {
        // The pairing is the only thing joining the two, so everything it claims is checked: both
        // ends exist, they are different entries, and the second really is written for a transport
        // the first is not — a pair whose halves share a channel would be a duplicate somebody
        // would eventually edit one of.
        MessageTemplateCatalog.SecondWordings.ShouldNotBeEmpty();

        foreach (var (message, wording) in MessageTemplateCatalog.SecondWordings)
        {
            message.ShouldNotBe(wording);

            var first = MessageTemplateCatalog.Find(message).ShouldNotBeNull(message);
            var second = MessageTemplateCatalog.Find(wording).ShouldNotBeNull(wording);

            second.Channel.ShouldNotBe(first.Channel, wording);
            MessageTemplateCatalog.IsSecondWording(wording).ShouldBeTrue(wording);

            // No chains. A wording is reached from the message it belongs to and from nowhere
            // else, so one that is itself somebody's message would be reachable two ways.
            MessageTemplateCatalog.IsSecondWording(message).ShouldBeFalse(message);
            MessageTemplateCatalog.SecondWordings.ShouldNotContain(pair => pair.Message == wording);
        }
    }

    [Fact]
    public void A_second_wording_may_say_no_more_than_the_message_it_is_a_wording_of()
    {
        // Both wordings are rendered from one bag of values, frozen by the producer at the moment
        // it queued the message it was writing — and the producer was written against the first
        // wording's declared list. A second wording naming anything outside that list asks for a
        // value nobody supplies, and the renderer leaves an unsupplied placeholder empty rather
        // than complaining, so the message would simply arrive with a hole where the fact was.
        foreach (var (message, wording) in MessageTemplateCatalog.SecondWordings)
        {
            var first = MessageTemplateCatalog.Find(message)!;
            var second = MessageTemplateCatalog.Find(wording)!;

            foreach (var placeholder in second.Placeholders)
            {
                first.Placeholders.ShouldContain(
                    placeholder,
                    $"{wording} declares {{{placeholder}}}, which {message} never carries");
            }
        }
    }

    [Fact]
    public void A_second_wording_is_swept_by_the_guards_that_hold_over_notifications()
    {
        // Every rule about what a notification may say is applied by reading the key: the ban on
        // a placeholder a coordinate could arrive in, and the two catalogue-wide sweeps over
        // language and declared placeholders, all find their subjects that way. So a wording of a
        // notification has to be named as one — this pins the reason those guards reach it, which
        // is the half that would rot silently if a later wording were named some other way.
        foreach (var (message, wording) in MessageTemplateCatalog.SecondWordings)
        {
            if (!message.StartsWith(NotificationTargetPolicy.TemplatePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            wording.StartsWith(NotificationTargetPolicy.TemplatePrefix, StringComparison.Ordinal)
                .ShouldBeTrue($"{wording} is a wording of a notification and must be named as one");

            MessageTemplateCatalog.All.ShouldContain(d => d.Key == wording);
        }
    }

    [Theory]
    [InlineData(
        MessageTemplateCatalog.NotifyGroupAnnouncement,
        MessageChannel.Email,
        MessageTemplateCatalog.NotifyGroupAnnouncement)]
    [InlineData(
        MessageTemplateCatalog.NotifyGroupAnnouncement,
        MessageChannel.Sms,
        MessageTemplateCatalog.NotifyGroupAnnouncementSms)]
    [InlineData(
        MessageTemplateCatalog.NotifyTripCalloutOverdue,
        MessageChannel.Email,
        MessageTemplateCatalog.NotifyTripCalloutOverdue)]
    [InlineData(
        MessageTemplateCatalog.NotifyTripCalloutOverdue,
        MessageChannel.Sms,
        MessageTemplateCatalog.NotifyTripCalloutOverdueSms)]
    public void Whatever_sends_is_told_which_wording_its_transport_can_read(
        string message, MessageChannel channel, string expected)
    {
        // Asserted through a non-null check rather than a null-conditional call: an answer of
        // null is exactly the regression this exists to catch, and a conditional one would skip
        // the assertion instead of failing on it.
        //
        // Both directions for each message, because the two halves fail differently: losing the
        // second wording sends nothing by text, and losing the first would send the mailbox
        // wording to a phone.
        MessageTemplateCatalog.On(message, channel)
            .ShouldNotBeNull()
            .Key.ShouldBe(expected);
    }

    [Fact]
    public void A_message_with_no_wording_for_a_transport_is_not_carried_by_it()
    {
        // The refusal is the point: without it whatever sends would have to fall back on the one
        // wording that exists, and a mailbox message — a greeting, an opt-out line and a blank
        // line between every paragraph — would be billed by the character to somebody's phone.
        MessageTemplateCatalog.On(MessageTemplateCatalog.NotifyCommentReply, MessageChannel.Sms)
            .ShouldBeNull();

        // And the positive case in the same breath, so a method that had simply stopped answering
        // could not pass this.
        MessageTemplateCatalog.On(MessageTemplateCatalog.NotifyCommentReply, MessageChannel.Email)
            .ShouldNotBeNull();

        MessageTemplateCatalog.On("notify.no-such-message", MessageChannel.Email).ShouldBeNull();
    }

    [Fact]
    public void An_announcement_by_text_says_that_one_arrived_and_where_to_look_and_nothing_of_it()
    {
        // A text leaves the installation and obeys none of its rules afterwards: it does not
        // expire, it cannot be withdrawn, and nothing re-checks the reader's access when they
        // finally look at it. The line somebody typed for a roster is exactly what a reader who
        // has since left the club must stop seeing, so it never enters the message — the notice
        // says that one arrived and where to read it, and the reading is where the check happens.
        //
        // Pinning the declared list is what makes that hold rather than describe today's wording:
        // the renderer refuses any placeholder off the list and an operator rewrite is refused the
        // same way, so the announcement can only reach a phone by being declared here first.
        var definition = MessageTemplateCatalog.Find(MessageTemplateCatalog.NotifyGroupAnnouncementSms)
            .ShouldNotBeNull();

        definition.Channel.ShouldBe(MessageChannel.Sms);
        definition.Placeholders.ShouldBe(
            ["appName", "actorName", "cavingGroupName", "url"],
            ignoreOrder: true);

        definition.Placeholders.ShouldNotContain("announcement");
        definition.Placeholders.ShouldNotContain("unsubscribeUrl");

        foreach (var locale in MessageTemplateCatalog.Locales)
        {
            var text = MessageTemplateCatalog.Default(definition, locale);
            text.Subject.ShouldBeNull($"{definition.Key} ({locale})");
            text.Body.ShouldNotBeNullOrWhiteSpace($"{definition.Key} ({locale})");
            text.Body.ShouldContain("{url}", customMessage: $"{definition.Key} ({locale})");
            text.Body.ShouldNotContain("\n", customMessage: $"{definition.Key} ({locale})");
        }
    }

    [Fact]
    public void An_overdue_alarm_by_text_says_which_party_and_by_when_and_nothing_more()
    {
        // Written as its own text rather than as the mailbox wording shortened, because this is
        // the one message whose reader may be standing at a cave entrance with no data: what is
        // legible before anything is opened is part of the safety argument. So it leads with
        // which party and what has not been confirmed, and the link — which nobody in a car park
        // can open anyway — comes last.
        //
        // Pinning the declared list is what makes the narrowing hold rather than describe today's
        // wording: the renderer refuses any placeholder off the list and an operator rewrite is
        // refused the same way, so nothing new can enter this message without being declared here
        // first. What must stay out is the greeting, because a phone already knows whose it is and
        // every character is billed, and the date on its own, because the hour that passed already
        // contains it.
        var definition = MessageTemplateCatalog.Find(MessageTemplateCatalog.NotifyTripCalloutOverdueSms)
            .ShouldNotBeNull();

        definition.Channel.ShouldBe(MessageChannel.Sms);
        definition.Placeholders.ShouldBe(["tripTitle", "expectedReturn", "url"], ignoreOrder: true);

        definition.Placeholders.ShouldNotContain("displayName");
        definition.Placeholders.ShouldNotContain("tripDate");
        definition.Placeholders.ShouldNotContain("unsubscribeUrl");

        foreach (var locale in MessageTemplateCatalog.Locales)
        {
            var text = MessageTemplateCatalog.Default(definition, locale);
            text.Subject.ShouldBeNull($"{definition.Key} ({locale})");
            text.Body.ShouldNotBeNullOrWhiteSpace($"{definition.Key} ({locale})");

            // Which party, first, so a truncated preview on a locked screen still names the trip
            // somebody has to act about.
            text.Body.ShouldStartWith("{tripTitle}", customMessage: $"{definition.Key} ({locale})");
            text.Body.ShouldContain("{expectedReturn}", customMessage: $"{definition.Key} ({locale})");
            text.Body.ShouldContain("{url}", customMessage: $"{definition.Key} ({locale})");
            text.Body.ShouldNotContain("\n", customMessage: $"{definition.Key} ({locale})");
        }
    }

    /// <summary>
    /// The characters a text message can carry seven bits at a time, in the alphabet the radio
    /// interface defines. Everything outside it — including every Romanian diacritic — forces the
    /// whole message into two bytes a character.
    /// </summary>
    private const string SevenBitAlphabet =
        "@£$¥èéùìòÇ\nØø\rÅå"
        + "Δ_ΦΓΛΩΠΨΣΘΞÆæßÉ"
        + " !\"#¤%&'()*+,-./0123456789:;<=>?"
        + "¡ABCDEFGHIJKLMNOPQRSTUVWXYZÄÖÑÜ§"
        + "¿abcdefghijklmnopqrstuvwxyzäöñüà";

    /// <summary>
    /// The few characters the seven-bit alphabet reaches only through an escape, and which
    /// therefore cost two units each rather than one.
    /// </summary>
    private const string SevenBitEscaped = "\f^{}\\[~]|€";

    /// <summary>
    /// How many parts the network would charge for <paramref name="message"/>, and in which
    /// encoding it would send it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A part carries 140 octets. Seven bits to the character that is 160 characters, or 153 once
    /// a part has to carry the header that lets a phone reassemble a split message; two bytes to
    /// the character it is 70, or 67 split. The encoding is chosen for the message as a whole, so
    /// a single character outside the seven-bit alphabet more than halves what the whole message
    /// can hold. That is why this counts parts and not characters: a character count would read
    /// the same for both languages and be right about neither.
    /// </para>
    /// <para>
    /// Two-byte length is counted in UTF-16 units rather than in characters, because that is what
    /// goes over the air — anything outside the basic multilingual plane costs two. The one thing
    /// deliberately not modelled is an escape falling across a part boundary; none of the wordings
    /// measured here contains an escaped character at all once its placeholders are filled in.
    /// </para>
    /// </remarks>
    private static (bool SevenBit, int Units, int Parts) Parts(string message)
    {
        var units = 0;
        var sevenBit = true;

        foreach (var character in message)
        {
            if (SevenBitAlphabet.Contains(character, StringComparison.Ordinal))
            {
                units += 1;
            }
            else if (SevenBitEscaped.Contains(character, StringComparison.Ordinal))
            {
                units += 2;
            }
            else
            {
                sevenBit = false;
                break;
            }
        }

        if (!sevenBit)
        {
            units = message.Length;
        }

        var single = sevenBit ? 160 : 70;
        var split = sevenBit ? 153 : 67;

        return (sevenBit, units, units <= single ? 1 : (units + split - 1) / split);
    }

    [Fact]
    public void The_overdue_alarm_costs_no_more_than_three_parts_in_either_language()
    {
        // Every part of this is billed to the installation, for every person a trip names, at the
        // moment somebody is overdue — so what the wording costs is decided here rather than
        // discovered on an invoice. It is counted in parts and not in characters because a part is
        // not a fixed number of characters: a message is carried seven bits to a character while
        // every character is in the carrier's own alphabet, 153 of them to a part, and one
        // character outside it carries the whole message two bytes to the character, where a part
        // holds 67. The switch is binary and it is decided by a single character, so a character
        // bound would pass a wording and hide its neighbour at more than double the price — which
        // is exactly the mistake worth failing a build over.
        //
        // Which alphabet applies is not a property of the language alone, and that is the part
        // that is easy to get backwards. Romanian's diacritics are outside it, so the Romanian
        // wording is always at half capacity; but a cave name spelled properly puts a diacritic
        // into the English message too, and then the English is at half capacity as well and is
        // the dearer of the two, because its fixed text is the longer. So both are measured, and
        // measured with a value that carries diacritics rather than one that hides the effect.
        //
        // Measured against values chosen to be the dear end of realistic rather than the kind
        // end: a cave name carrying diacritics, a full timestamp, and an installation reachable
        // at its own domain rather than the development default. The link and the hour together
        // are ninety-five characters the wording does not choose and cannot shorten, and two
        // parts hold a hundred and thirty-four — so two parts is not reachable by any wording that
        // also names which party is overdue. Three is therefore the honest bound, and buying the
        // fourth back by writing less would be spending legibility on postage for the one message
        // where being legible unopened is the whole point.
        var definition = MessageTemplateCatalog
            .Find(MessageTemplateCatalog.NotifyTripCalloutOverdueSms)
            .ShouldNotBeNull();

        var values = new Dictionary<string, string>
        {
            ["tripTitle"] = "Peștera Ursilor",
            ["expectedReturn"] = "2026-08-27 21:30 UTC",
            ["url"] = "https://silexgis.example.org/trip-logs/8f3a1c2e-4b5d-6a7f-8091-a2b3c4d5e6f7",
        };

        foreach (var locale in MessageTemplateCatalog.Locales)
        {
            var body = MessageTemplateRenderer.Render(
                MessageTemplateCatalog.Default(definition, locale).Body, values);
            var (_, units, parts) = Parts(body);

            parts.ShouldBeLessThanOrEqualTo(
                3,
                $"{definition.Key} ({locale}) is {parts} parts, {units} units: {body}");
        }

        // The second half, and the one that keeps the first honest. A bound on parts can always be
        // met by taking the diacritics out of the Romanian, which would make it seven-bit and
        // halve its price — and would also make it wrong, so it is refused here rather than left
        // as a temptation for whoever is next asked to make this cheaper.
        var romanian = MessageTemplateCatalog.Default(definition, "ro").Body;
        Parts(romanian).SevenBit.ShouldBeFalse(
            "the Romanian wording is written in Romanian, and correct spelling is not negotiable "
            + "against the price of a message");

        // And the discipline that stops the expensive language becoming an afterthought: the
        // Romanian is written first and the English follows it, so the language billed at double
        // is never the longer of the two. Held as an invariant rather than as a habit, because it
        // is the half that goes wrong silently — English has more than twice the room, so an
        // English wording can grow for a long time before anything complains.
        var english = MessageTemplateCatalog.Default(definition, "en").Body;
        romanian.Length.ShouldBeLessThanOrEqualTo(
            english.Length,
            $"ro is {romanian.Length} characters against en at {english.Length}");
    }

    [Fact]
    public void The_overdue_alarm_says_how_long_a_trip_title_it_can_still_carry()
    {
        // The bound above is a property of the wording and of one representative title. The title
        // itself is free text somebody typed when they planned the trip, and it goes into the
        // message exactly as it stands, so how much of it fits is what decides whether a real
        // alarm is three parts or four. Measured here in the one direction a build can check: how
        // many characters of title each wording still has room for. Held above a floor rather than
        // pinned to a number, so shortening the fixed text is free while lengthening it has to be
        // paid for by admitting which titles it stops carrying.
        //
        // Measured with a title made of characters outside the carrier's seven-bit alphabet,
        // because a cave name spelled properly is written with them and that is the case that
        // costs. What this does not do is bound the message that actually leaves: nothing shortens
        // a long title on the way out, so a title longer than the room measured here is charged
        // the extra part, for every person the trip names.
        const int floorCharacters = 40;

        var definition = MessageTemplateCatalog
            .Find(MessageTemplateCatalog.NotifyTripCalloutOverdueSms)
            .ShouldNotBeNull();

        foreach (var locale in MessageTemplateCatalog.Locales)
        {
            var template = MessageTemplateCatalog.Default(definition, locale).Body;

            // Counted upwards rather than solved for, because the relation is a step function and
            // a loop that anybody can read is worth more here than an arithmetic one that has to
            // be trusted. Stopped well short of the title column's own maximum so a wording that
            // somehow fitted everything ends the loop rather than running away with it.
            var carried = 0;
            while (carried < 300 && TitleFits(template, carried + 1))
            {
                carried++;
            }

            carried.ShouldBeGreaterThanOrEqualTo(
                floorCharacters,
                $"{definition.Key} ({locale}) has room for {carried} characters of trip title "
                + "before it costs a fourth part");
        }

        static bool TitleFits(string template, int titleLength)
        {
            var body = MessageTemplateRenderer.Render(template, new Dictionary<string, string>
            {
                ["tripTitle"] = new string('\u0103', titleLength),
                ["expectedReturn"] = "2026-08-27 21:30 UTC",
                ["url"] = "https://silexgis.example.org/trip-logs/8f3a1c2e-4b5d-6a7f-8091-a2b3c4d5e6f7",
            });

            var (_, _, parts) = Parts(body);
            return parts <= 3;
        }
    }

    [Theory]
    [InlineData("ro", "ro")]
    [InlineData("ro-RO", "ro")]
    [InlineData("en-GB", "en")]
    [InlineData("de", "en")]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    public void A_locale_is_reduced_to_one_the_catalogue_has(string? input, string expected)
    {
        MessageTemplateCatalog.Normalise(input).ShouldBe(expected);
    }

    [Fact]
    public void A_language_a_template_lacks_falls_back_to_english()
    {
        var definition = MessageTemplateCatalog.Find(MessageTemplateCatalog.EmailPasswordReset)!;

        MessageTemplateCatalog.Default(definition, "de")
            .ShouldBe(MessageTemplateCatalog.Default(definition, "en"));
    }

    [Fact]
    public void Tidy_collapses_the_gap_a_missing_value_leaves_behind()
    {
        MessageTemplateRenderer.Tidy("One\n\n\n\nTwo\n\n").ShouldBe("One\n\nTwo");
    }

    /// <summary>
    /// Fragments of a name that would be one: a bearing, a projected pair, a height. Matched as
    /// substrings and case-insensitively, so a placeholder called "caveLat" or "utmEasting" is
    /// caught as surely as one called "latitude".
    /// </summary>
    private static readonly string[] CoordinateShaped =
    [
        "lat", "lon", "coord", "position", "wgs", "utm", "easting", "northing",
        "elevation", "altitude", "gps",
    ];

    [Fact]
    public void No_notification_declares_a_placeholder_a_coordinate_could_arrive_in()
    {
        // Where a cave is, is readable by fewer people than the fact that something happened to
        // it, and a message leaves the installation entirely: once it is in a mailbox it obeys
        // none of the rules that decide who may see a position. So no notification may carry one.
        //
        // Pinning the declared list is what makes that a rule rather than a hope. A placeholder
        // off the list is refused when an operator saves a rewrite, and renders as nothing if it
        // somehow got in, so nothing can reach the wording without being declared here first —
        // and a producer supplying a value the wording never names is dropped silently.
        //
        // What this cannot do, and nothing else can either: a producer is free to write a
        // coordinate into a value the message *does* declare, because an object's name is text
        // somebody typed and "Peștera 45.1234, 25.5678" is a name a person may genuinely have
        // given a cave. The only guard there is the producer contract — a producer names the
        // thing and links to it, and never describes where it is.
        foreach (var definition in MessageTemplateCatalog.All)
        {
            if (!definition.Key.StartsWith(NotificationTargetPolicy.TemplatePrefix, StringComparison.Ordinal))
            {
                continue;
            }

            foreach (var placeholder in definition.Placeholders)
            {
                foreach (var fragment in CoordinateShaped)
                {
                    placeholder.Contains(fragment, StringComparison.OrdinalIgnoreCase)
                        .ShouldBeFalse($"{definition.Key} declares {{{placeholder}}}");
                }
            }
        }
    }
}
