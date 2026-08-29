// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Shouldly;
using SilexGis.Domain.Messaging;

namespace SilexGis.Domain.Tests;

/// <summary>
/// Every message an operator can rewrite has a sentence naming it, in every language the browser
/// speaks.
/// </summary>
/// <remarks>
/// <para>
/// The page that lists the wording an operator may edit renders each row by looking its key up in
/// the translation files and falling back to the raw key when the lookup misses. The fallback is
/// right — a page that threw would hide every other row — but it is silent, so a message added to
/// the catalogue without a sentence beside it ships as <c>notify.something-or-other</c> in the
/// middle of an otherwise readable list, and nothing on either side complains.
/// </para>
/// <para>
/// The catalogue is the only list of what exists, and it lives here rather than in the browser, so
/// this is where the two can be compared at all. The alternative — a second list of keys kept in
/// the front end — would be the thing that drifts instead.
/// </para>
/// </remarks>
public class MessageTemplateDisplayNameTests
{
    [Theory]
    [InlineData("en")]
    [InlineData("ro")]
    public void Every_message_in_the_catalogue_is_named_in_this_language(string locale)
    {
        var names = TemplateNames(locale);

        names.ShouldNotBeEmpty($"no template names were found for {locale}");

        foreach (var definition in MessageTemplateCatalog.All)
        {
            // The lookup the page performs, which is the key with every separator turned into the
            // one character a translation key may contain.
            var lookup = definition.Key.Replace('.', '_').Replace('-', '_');

            names.ShouldContainKey(
                lookup,
                $"{definition.Key} has no name in {locale}; the operator would see the raw key");
            names[lookup].ShouldNotBeNullOrWhiteSpace(definition.Key);
        }
    }

    [Fact]
    public void No_name_is_left_behind_for_a_message_that_no_longer_exists()
    {
        // The other way the list rots. A name nobody can reach is not a fault a reader ever sees,
        // which is exactly why it survives being renamed around.
        var reachable = MessageTemplateCatalog.All
            .Select(d => d.Key.Replace('.', '_').Replace('-', '_'))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var locale in new[] { "en", "ro" })
        {
            foreach (var name in TemplateNames(locale).Keys)
            {
                reachable.ShouldContain(name, $"{locale} names a message the catalogue does not have");
            }
        }
    }

    /// <summary>
    /// The <c>name_*</c> entries of one language's translation file, keyed the way the page looks
    /// them up.
    /// </summary>
    private static Dictionary<string, string?> TemplateNames(string locale)
    {
        var path = Path.Combine(RepositoryRoot(), "client", "src", "i18n", "locales", $"{locale}.json");
        File.Exists(path).ShouldBeTrue($"the translation file for {locale} was not found at {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var templates = document.RootElement.GetProperty("admin").GetProperty("templates");

        return templates.EnumerateObject()
            .Where(p => p.Name.StartsWith("name_", StringComparison.Ordinal))
            .ToDictionary(
                p => p.Name["name_".Length..],
                p => p.Value.GetString(),
                StringComparer.Ordinal);
    }

    /// <summary>
    /// The checkout both halves of the application sit in, found by walking up from the test
    /// assembly rather than assumed, so the path holds wherever the build output lands.
    /// </summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "client", "src", "i18n", "locales"))
                && Directory.Exists(Path.Combine(directory.FullName, "server", "src")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"no checkout containing both halves of the application was found above {AppContext.BaseDirectory}");
    }
}
