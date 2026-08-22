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
