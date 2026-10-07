// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Api.Tests.Support;
using SilexGis.Infrastructure.Files;

namespace SilexGis.Api.Tests;

/// <summary>
/// What a test has to itself in each of the two modes this suite runs in.
/// </summary>
/// <remarks>
/// <para>
/// Every other class here trusts the answer without looking: in precise mode that the application
/// it talks to was built for this test and no other, in fast mode that the one it was handed is
/// the class's and still gives it the few things it was promised. Neither can be seen from a
/// result — a suite that quietly shared applications in precise mode would be green and wrong, and
/// one that shared nothing in fast mode would be green and slow — so they are asserted here, in
/// whichever mode the run is in.
/// </para>
/// <para>
/// The statics are the instrument, not a shortcut: xunit builds this class again for every test,
/// so only something static can see two tests at once.
/// </para>
/// </remarks>
public sealed class TestModeTests : IClassFixture<PostgresFixture>, IDisposable
{
    private static readonly HashSet<IServiceProvider> TakingTurns = new(ReferenceEqualityComparer.Instance);
    private static readonly HashSet<IServiceProvider> KeptApart = new(ReferenceEqualityComparer.Instance);
    private static int turnsTaken;
    private static int timesKeptApart;

    private readonly string connectionString;
    private readonly string filesRoot;
    private readonly Dictionary<string, string?> settings;
    private readonly SilexGisApiFactory factory;
    private readonly SilexGisApiFactory beside;
    private readonly SilexGisApiFactory apart;

    public TestModeTests(PostgresFixture postgres)
    {
        connectionString = postgres.ConnectionString;

        // Named the way most classes name theirs: a directory nobody has made, new for each test.
        filesRoot = Path.Combine(TestScratch.Root, $"silexgis-test-modes-{Guid.NewGuid():N}");
        settings = new Dictionary<string, string?>
        {
            ["Files:Root"] = filesRoot,
            ["Keys:Path"] = Path.Combine(filesRoot, "keys"),
        };
        factory = new SilexGisApiFactory(connectionString, settings);

        // A second application on the same database and the same directory, as a class testing two
        // nodes of one installation makes; and one that asks to be nobody else's.
        beside = new SilexGisApiFactory(connectionString, settings);
        apart = new SilexGisApiFactory(connectionString, ownHost: true);
    }

    public void Dispose()
    {
        factory.Dispose();
        beside.Dispose();
        apart.Dispose();
        if (Directory.Exists(filesRoot))
        {
            Directory.Delete(filesRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_tests_of_a_class_take_turns_on_one_application_only_in_fast_mode(int turn)
    {
        TakingTurns.Add(factory.Services);
        turnsTaken++;

        TakingTurns.Count.ShouldBe(
            TestMode.Fast ? 1 : turnsTaken,
            $"turn {turn}: an application for every test in precise mode, one for the class in fast mode");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void An_application_asked_for_as_the_tests_own_is_built_for_it_in_either_mode(int turn)
    {
        KeptApart.Add(apart.Services);
        timesKeptApart++;

        KeptApart.Count.ShouldBe(timesKeptApart, $"turn {turn}");
        apart.Services.ShouldNotBeSameAs(factory.Services);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Nothing_sent_before_a_test_began_is_in_its_record(int turn)
    {
        // What the application sends lands in the record this test reads, borrowed or not.
        factory.Services.GetRequiredService<MessageCapture>().ShouldBeSameAs(factory.Messages);

        factory.Messages.Messages.ShouldBeEmpty($"turn {turn}");
        factory.Messages.MailConfigured.ShouldBeTrue();
        factory.Messages.SmsConfigured.ShouldBeTrue();
        factory.Messages.FailSendsTo.ShouldBeNull();

        // Left behind for the next turn to not find.
        factory.Messages.Record("email", "somebody@t.local", "left behind", "by an earlier test");
        factory.Messages.MailConfigured = false;
        factory.Messages.SmsConfigured = false;
        factory.Messages.FailSendsTo = "nobody@t.local";
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Nothing_the_application_cached_for_an_earlier_test_is_still_cached(int turn)
    {
        // The settings and the message templates are cached this way, and classes put them back
        // by writing to the database underneath the application — which only a cache that has
        // been emptied, or an application that has just started, would notice.
        var cache = factory.Services.GetRequiredService<IMemoryCache>();

        cache.TryGetValue("left by an earlier test", out _).ShouldBeFalse($"turn {turn}");

        cache.Set("left by an earlier test", turn);
    }

    [Fact]
    public void The_directory_a_test_names_is_the_one_its_application_writes_to()
    {
        var written = Path.Combine(RootOf(factory), $"{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(RootOf(factory));
        File.WriteAllText(written, "where the application keeps its files");

        // Seen through the path the test made up — so a test that looks there for what an upload
        // wrote, or should not have written, is looking at the real store.
        File.Exists(Path.Combine(filesRoot, Path.GetFileName(written))).ShouldBeTrue();

        // And a test that clears its directory away takes nothing from a store that is the class's.
        Directory.Delete(filesRoot, recursive: true);
        Directory.Exists(filesRoot).ShouldBeFalse();
        File.Exists(written).ShouldBe(TestMode.Fast);
    }

    [Fact]
    public void A_second_application_made_beside_the_first_is_another_one_on_the_same_directory()
    {
        beside.Services.ShouldNotBeSameAs(factory.Services);

        var written = Path.Combine(RootOf(factory), $"{Guid.NewGuid():N}.txt");
        Directory.CreateDirectory(RootOf(factory));
        File.WriteAllText(written, "written by one node");

        File.Exists(Path.Combine(RootOf(beside), Path.GetFileName(written))).ShouldBeTrue();
    }

    [Fact]
    public void An_application_a_test_builds_for_itself_stops_when_the_test_lets_go_of_it()
    {
        // The same request the class's constructor made, made here: inside a test it is never
        // answered with the class's application, because a test that builds one mid-way is about
        // what a newly started one does.
        IServiceProvider mine;
        using (var made = new SilexGisApiFactory(connectionString))
        {
            mine = made.Services;
            mine.ShouldNotBeSameAs(factory.Services);
        }

        Should.Throw<ObjectDisposedException>(() => mine.GetRequiredService<IConfiguration>());
    }

    [Fact]
    public void Letting_go_of_the_class_application_stops_it_only_when_it_was_this_tests_own()
    {
        var services = factory.Services;

        factory.Dispose();

        if (TestMode.Fast)
        {
            // Still running for the next test of the class; the database fixture stops it.
            services.GetRequiredService<IConfiguration>().ShouldNotBeNull();
        }
        else
        {
            Should.Throw<ObjectDisposedException>(() => services.GetRequiredService<IConfiguration>());
        }
    }

    [Fact]
    public void An_application_configured_further_is_built_for_the_test_and_stops_with_it()
    {
        var further = factory.WithWebHostBuilder(builder => builder.UseSetting("Auth:RateLimitPerMinute", "7"));
        var services = further.Services;

        services.ShouldNotBeSameAs(factory.Services);
        services.GetRequiredService<IConfiguration>()["Auth:RateLimitPerMinute"].ShouldBe("7");
        factory.Services.GetRequiredService<IConfiguration>()["Auth:RateLimitPerMinute"].ShouldBe("100000");

        factory.Dispose();

        Should.Throw<ObjectDisposedException>(() => services.GetRequiredService<IConfiguration>());
    }

    [Fact]
    public void Services_a_test_hands_over_are_in_the_application_it_gets()
    {
        // Two factories asking alike, each handing over an object of its own. Sharing by what the
        // request looks like would hand the second the first one's application — and with it the
        // first one's object, which the second test never sees and cannot assert on.
        var first = new Handed();
        var second = new Handed();
        using var one = new MadeByAConstructor(connectionString, services => services.AddSingleton(first));
        one.Factory.Services.GetRequiredService<Handed>().ShouldBeSameAs(first);
        one.Dispose();

        using var two = new MadeByAConstructor(connectionString, services => services.AddSingleton(second));
        two.Factory.Services.GetRequiredService<Handed>().ShouldBeSameAs(second);
    }

    [Fact]
    public void Services_that_hold_nothing_of_the_test_do_not_keep_an_application_from_being_shared()
    {
        IServiceProvider earlier;
        using (var one = new MadeByAConstructor(connectionString, services => services.AddSingleton(new Handed())))
        {
            earlier = one.Factory.Services;
        }

        using var two = new MadeByAConstructor(connectionString, services => services.AddSingleton(new Handed()));

        // Two different lambdas, so two different changes: never the same application.
        two.Factory.Services.ShouldNotBeSameAs(earlier);

        using var again = MadeByAConstructor.WithNothingOfTheTests(connectionString);
        var once = again.Factory.Services;
        again.Dispose();
        using var andAgain = MadeByAConstructor.WithNothingOfTheTests(connectionString);
        ReferenceEquals(andAgain.Factory.Services, once).ShouldBe(TestMode.Fast);
    }

    private static string RootOf(SilexGisApiFactory application) =>
        application.Services.GetRequiredService<IOptions<FilesOptions>>().Value.Root;

    /// <summary>Something a test puts into its application to find again.</summary>
    private sealed class Handed;

    /// <summary>
    /// Makes a factory the way a test class does — from a constructor — so that a single test can
    /// stand in for two tests of a class asking one after the other.
    /// </summary>
    private sealed class MadeByAConstructor : IDisposable
    {
        public MadeByAConstructor(string connectionString, Action<IServiceCollection> configureServices) =>
            Factory = new SilexGisApiFactory(connectionString, configureServices: configureServices);

        public SilexGisApiFactory Factory { get; }

        /// <summary>The same lambda each time, and one that refers to nothing around it.</summary>
        public static MadeByAConstructor WithNothingOfTheTests(string connectionString) =>
            new(connectionString, services => services.AddSingleton(new Handed()));

        public void Dispose() => Factory.Dispose();
    }
}
