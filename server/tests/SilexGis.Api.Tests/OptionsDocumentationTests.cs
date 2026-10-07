// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Shouldly;
using SilexGis.Api.Common;

namespace SilexGis.Api.Tests;

/// <summary>
/// Every setting the API binds is named in the two documents an operator reads.
/// </summary>
/// <remarks>
/// <para>
/// <b>What this is for.</b> A setting is added in one place — a property on an options class —
/// and has to be written down in two others by hand: the configuration reference of the
/// installation guide, which calls itself "the list to read", and the sample environment file,
/// which is where a value is actually typed. Nothing connected the three, so the list was
/// complete on the day somebody last checked it and no later. An operator cannot set what they
/// cannot find, and a default that discloses something, or bounds an anonymous route, is exactly
/// the kind of setting that must be findable.
/// </para>
/// <para>
/// <b>What it reads.</b> Every class of the API's own assembly that carries a section name, and
/// every public property of it that can be set — which is what the configuration binder fills.
/// Each must appear in both documents under the name an operator would type,
/// <c>SILEXGIS__Section__Property</c>, written out in full: a shorthand such as "<c>…__Email</c>
/// / <c>__Password</c>" cannot be searched for and does not count. A list is named by an
/// element of it (<c>…__0</c>, or any other number), a list of structured entries by each member
/// of an element, and a table of named values by its prefix.
/// </para>
/// <para>
/// <b>The one translation.</b> The packaged stack sets some of these itself, from a variable of
/// its own with a shorter name (<c>SILEXGIS_ADMIN_EMAIL</c> for <c>SILEXGIS__Admin__Email</c>).
/// Whatever the stack sets wins over a line of the same setting typed into the environment
/// file, so for those the sample environment file must name the stack's variable — naming the
/// long form there would document a line that does nothing. Which settings these are is read
/// from the stack's own definition rather than listed here.
/// </para>
/// <para>
/// <b>What it does not read.</b> The handful of settings read by key rather than through a class
/// are listed here by hand. The
/// options classes of the layer underneath are not covered. And this checks that a name is
/// present, not that what is said about it is true.
/// </para>
/// <para>
/// When it fails, the fix is a row and a paragraph in the documents. The list of exemptions is
/// for a setting that deliberately has no place in an operator's hands, and each entry says why.
/// </para>
/// </remarks>
public class OptionsDocumentationTests
{
    /// <summary>
    /// Settings that are bound and deliberately not offered to an operator, each with its reason.
    /// Empty is the state to keep; an entry here is a decision, not a postponement.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> Undocumented = new Dictionary<string, string>
    {
    };

    /// <summary>Settings read by key, with no options class to reflect over.</summary>
    private static readonly string[] ReadByKey =
    [
        "Auth:RateLimitPerMinute",
        "Qr:RateLimitPerMinute",
        "TripTracking:PublicRateLimitPerMinute",
        "CalendarFeed:RateLimitPerMinute",
        TrustedProxies.HopsKey,
        TrustedProxies.NetworksKey + ":0",
    ];

    public static TheoryData<string> Documents() => new()
    {
        Path.Combine("docs", "INSTALL.md"),
        Path.Combine("deploy", ".env.example"),
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public void Every_setting_the_application_binds_is_named_in_the_document(string document)
    {
        var text = File.ReadAllText(Path.Combine(WorkingTree(), document));
        // Only the environment file is read by the stack; the guide names the setting itself.
        var typedAs = document.EndsWith(".env.example", StringComparison.Ordinal)
            ? SetByTheStack(File.ReadAllText(Path.Combine(WorkingTree(), "deploy", "docker-compose.yml")))
            : new Dictionary<string, string>();

        var missing = Settings()
            .Where(setting => !Undocumented.ContainsKey(setting))
            .Select(setting => typedAs.GetValueOrDefault(setting, setting))
            .Where(setting => !Names(text, setting))
            .Order(StringComparer.Ordinal)
            .ToList();

        missing.ShouldBeEmpty(
            $"{document} does not name these settings, each of which the application binds. Add them — "
            + "to both documents — written out in full:\n  " + string.Join("\n  ", missing) + "\n");
    }

    [Fact]
    public void The_settings_found_include_ones_known_to_exist()
    {
        // If the reflection below ever found nothing — a renamed constant, a moved assembly — the
        // test above would pass on an empty list. These are settings of four different shapes that
        // must be among what it finds.
        var settings = Settings();

        settings.ShouldContain("SILEXGIS__TripTracking__ShareLifetime");
        settings.ShouldContain("SILEXGIS__TripPastTracks__Retention");
        settings.ShouldContain("SILEXGIS__TripTracking__PublicRateLimitPerMinute");
        settings.ShouldContain("SILEXGIS__Proxy__TrustedNetworks__0");
        settings.ShouldContain("SILEXGIS__Auth__AdditionalRedirectUris__0");
        settings.ShouldContain("SILEXGIS__Auth__ExternalProviders__0__ClientId");
        settings.ShouldContain("SILEXGIS__MapLayers__ApiKeys__");
        settings.Count.ShouldBeGreaterThan(50);

        // A computed property is not a setting: nothing binds it.
        settings.ShouldNotContain("SILEXGIS__TripTracking__EffectiveFollowedListSize");
    }

    [Fact]
    public void A_name_counts_only_when_it_is_written_out_whole()
    {
        // The property of the check itself that the first test depends on: a longer setting must
        // not vouch for a shorter one that begins it, and a shorthand must not vouch for anything.
        Names("| `SILEXGIS__Map__CenterlineMaxPathsLimit` |", "SILEXGIS__Map__CenterlineMaxPaths").ShouldBeFalse();
        Names("| `SILEXGIS__Map__CenterlineMaxPaths` |", "SILEXGIS__Map__CenterlineMaxPaths").ShouldBeTrue();
        Names("# SILEXGIS__Map__CenterlineMaxPaths=25000", "SILEXGIS__Map__CenterlineMaxPaths").ShouldBeTrue();
        Names("`SILEXGIS__Admin__Email` / `__Password`", "SILEXGIS__Admin__Password").ShouldBeFalse();
        Names("SILEXGIS__MapLayers__ApiKeys__thunderforest=", "SILEXGIS__MapLayers__ApiKeys__").ShouldBeTrue();
        Names("SILEXGIS__Proxy__TrustedNetworks__0=10.0.0.0/8", "SILEXGIS__Proxy__TrustedNetworks__0").ShouldBeTrue();
        // An element of a list is an element whatever its number, and still not a longer name.
        Names("SILEXGIS__Auth__ExternalProviders__2__Authority=https://sso", "SILEXGIS__Auth__ExternalProviders__0__Authority").ShouldBeTrue();
        Names("SILEXGIS__Auth__ExternalProviders__2__AuthorityHint=", "SILEXGIS__Auth__ExternalProviders__0__Authority").ShouldBeFalse();
        Names("SILEXGIS__Auth__ExternalProviders__x__Authority=", "SILEXGIS__Auth__ExternalProviders__0__Authority").ShouldBeFalse();
    }

    [Fact]
    public void A_setting_the_stack_sets_from_its_own_variable_is_typed_as_that_variable()
    {
        var typedAs = SetByTheStack(
            """
            services:
              api:
                environment:
                  SILEXGIS__Admin__Email: ${SILEXGIS_ADMIN_EMAIL:-}
                  SILEXGIS__Files__Root: /data/files
                  SILEXGIS__Web__FrameAncestors: "${SILEXGIS_FRAME_ANCESTORS:-}"
            """);

        typedAs["SILEXGIS__Admin__Email"].ShouldBe("SILEXGIS_ADMIN_EMAIL");
        typedAs["SILEXGIS__Web__FrameAncestors"].ShouldBe("SILEXGIS_FRAME_ANCESTORS");
        // A fixed value is the stack's own business and translates to nothing.
        typedAs.ShouldNotContainKey("SILEXGIS__Files__Root");

        // And the real definition does translate the settings this test relies on it for: if the
        // stack stopped setting them, the long form would be the line to type and to document.
        var shipped = SetByTheStack(File.ReadAllText(Path.Combine(WorkingTree(), "deploy", "docker-compose.yml")));
        shipped["SILEXGIS__Admin__Email"].ShouldBe("SILEXGIS_ADMIN_EMAIL");
        shipped["SILEXGIS__Admin__Password"].ShouldBe("SILEXGIS_ADMIN_PASSWORD");
    }

    [Fact]
    public void Nothing_is_exempted_that_no_longer_exists()
    {
        // An exemption that outlives its setting is a line nobody will ever remove.
        var settings = Settings();

        Undocumented.Keys.Where(exempt => !settings.Contains(exempt)).ShouldBeEmpty();
    }

    /// <summary>
    /// Whether a document names a setting: the name itself, not followed by more of a name. A name
    /// that ends in the separator is a prefix and is followed by whatever the operator chooses.
    /// </summary>
    private static bool Names(string text, string setting) =>
        setting.EndsWith("__", StringComparison.Ordinal)
            ? text.Contains(setting, StringComparison.Ordinal)
            : Regex.IsMatch(
                text,
                Regex.Escape(setting).Replace("__0", "__[0-9]+", StringComparison.Ordinal) + "(?![A-Za-z0-9_])");

    /// <summary>
    /// The settings the packaged stack sets from a variable of its own, and that variable: lines of
    /// the form <c>SILEXGIS__Section__Key: ${SILEXGIS_NAME…}</c> in the stack's definition.
    /// </summary>
    private static Dictionary<string, string> SetByTheStack(string compose) =>
        Regex.Matches(compose, @"^\s+(SILEXGIS__[A-Za-z0-9_]+):\s*""?\$\{(SILEXGIS_[A-Z0-9_]+)", RegexOptions.Multiline)
            // The same setting may be set for more than one service; the variable is the same.
            .GroupBy(match => match.Groups[1].Value)
            .ToDictionary(group => group.Key, group => group.First().Groups[2].Value);

    private static IReadOnlySet<string> Settings()
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var type in typeof(Program).Assembly.GetTypes())
        {
            var section = type.GetField("SectionName", BindingFlags.Public | BindingFlags.Static);
            if (!type.IsClass || section is not { IsLiteral: true } || section.FieldType != typeof(string)) continue;

            Collect(found, Variable((string)section.GetRawConstantValue()!), type);
        }

        foreach (var key in ReadByKey)
        {
            found.Add(Variable(key));
        }

        return found;
    }

    private static string Variable(string key) => "SILEXGIS__" + key.Replace(":", "__", StringComparison.Ordinal);

    private static void Collect(ISet<string> found, string prefix, Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is not { IsPublic: true }) continue;

            var name = $"{prefix}__{property.Name}";
            var kind = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (IsValue(kind))
            {
                found.Add(name);
            }
            else if (typeof(IDictionary).IsAssignableFrom(kind))
            {
                // Keyed by whatever the operator names; only the prefix can be promised.
                found.Add(name + "__");
            }
            else if (ElementOf(kind) is { } element)
            {
                if (IsValue(element)) found.Add(name + "__0");
                else Collect(found, name + "__0", element);
            }
            else
            {
                Collect(found, name, kind);
            }
        }
    }

    private static bool IsValue(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
        || type == typeof(TimeSpan) || type == typeof(DateTimeOffset) || type == typeof(DateTime)
        || type == typeof(Guid) || type == typeof(Uri) || type == typeof(TimeOnly) || type == typeof(DateOnly);

    private static Type? ElementOf(Type type) =>
        type.IsArray
            ? type.GetElementType()
            : type.GetInterfaces().Append(type)
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                ?.GetGenericArguments()[0];

    /// <summary>
    /// The working tree, found by walking up from the assembly: the documents are the ones beside
    /// the code under test, not copies in a build output directory.
    /// </summary>
    private static string WorkingTree()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "server", "SilexGis.slnx")))
        {
            directory = directory.Parent;
        }

        directory.ShouldNotBeNull("Could not find the working tree above the test assembly.");
        return directory.FullName;
    }
}
