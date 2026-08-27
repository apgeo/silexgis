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
    [InlineData(MessageChannel.Email, MessageTemplateCatalog.NotifyGroupAnnouncement)]
    [InlineData(MessageChannel.Sms, MessageTemplateCatalog.NotifyGroupAnnouncementSms)]
    public void Whatever_sends_is_told_which_wording_its_transport_can_read(
        MessageChannel channel, string expected)
    {
        // Asserted through a non-null check rather than a null-conditional call: an answer of
        // null is exactly the regression this exists to catch, and a conditional one would skip
        // the assertion instead of failing on it.
        MessageTemplateCatalog.On(MessageTemplateCatalog.NotifyGroupAnnouncement, channel)
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
