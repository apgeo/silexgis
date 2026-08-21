// SPDX-License-Identifier: AGPL-3.0-or-later
using Shouldly;
using SilexGis.Domain.Messaging;

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
            ["appName", "displayName", "actorName", "tripTitle", "tripDate", "siteUrl", "url", "unsubscribeUrl"],
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
    public void A_message_a_scheduled_pass_sends_names_no_actor_and_no_cave()
    {
        // Neither of these is caused by anybody, so neither declares an actor — a placeholder
        // nothing fills is one an operator can write into the wording and get a hole from. And
        // neither may name a cave. The temptation is at its worst on the overdue one, where an
        // alarm feels like the message that ought to say where the party is; but it goes to
        // everybody the trip names, and where a cave is stays readable by fewer people than that.
        var reminder = MessageTemplateCatalog.Find(MessageTemplateCatalog.NotifyTripPlanReminder)!;

        reminder.Placeholders.ShouldBe(
            ["appName", "displayName", "tripTitle", "tripDate", "siteUrl", "url", "unsubscribeUrl"],
            ignoreOrder: true);

        var overdue = MessageTemplateCatalog.Find(MessageTemplateCatalog.NotifyTripCalloutOverdue)!;

        // The overdue one carries no opt-out line, the way the account-security messages do not:
        // nobody may switch a callout off, so a link that could not work would be a lie. Leaving
        // the placeholder undeclared is what stops an operator putting one back.
        overdue.Placeholders.ShouldBe(
            ["appName", "displayName", "tripTitle", "tripDate", "expectedReturn", "siteUrl", "url"],
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
}
