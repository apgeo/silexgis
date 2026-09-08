// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Diagnostics;
using System.Net;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Tests;

/// <summary>
/// Asking a neighbouring photo library what state it is in.
///
/// <para>
/// No database and no host: the clients are built directly over a recording stub, because what is
/// under test is which routes are asked, what is made of the answers, and — the assertion that
/// carries most of these — that a library which is not there produces a <em>result</em> rather than
/// an exception. An installation with a wrong address, a revoked credential or a stopped container
/// is indistinguishable from a healthy one until somebody opens the map and finds it empty, so
/// every case here is a way of being broken that used to look exactly like working.
/// </para>
/// <para>
/// Every address, identifier, key and permission list below is invented. Nothing here comes from
/// any real library.
/// </para>
/// </summary>
public sealed class PhotoLibraryProbeTests
{
    private const string PrismAddress = "http://photo-library.invalid:2342";
    private const string ImmichAddress = "http://other-photo-library.invalid:2283";
    private const string FakeToken = "not-a-real-token-0000";
    private const string FakeKey = "not-a-real-key-0000";

    /// <summary>The routes each product is asked about itself, and nothing else.</summary>
    private const string PrismLiveness = "/api/v1/status";
    private const string PrismConfiguration = "/api/v1/config";
    private const string PrismPositions = "/api/v1/geo";
    private const string ImmichVersion = "/api/server/version";
    private const string ImmichKey = "/api/api-keys/me";

    // -------------------------------------------------------------- nothing asked is not a failure

    /// <summary>
    /// A library no operator has configured is not a library that failed, and the difference is the
    /// whole reason the answer has three values rather than two. The proof that matters is the last
    /// line: not one socket was opened.
    /// </summary>
    [Fact]
    public async Task An_unconfigured_library_is_asked_nothing_and_reports_that_nothing_was_asked()
    {
        var stub = new LibraryStub();

        var health = await Prism(stub, options => options.Enabled = false).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Unknown);
        health.ProbedAt.ShouldBeNull();
        health.FailureCode.ShouldBeNull();
        health.Version.ShouldBeNull();
        health.MissingPermissions.ShouldBeEmpty();

        stub.Asked.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------------- what is asked

    /// <summary>
    /// Two routes, and neither of them a picture. Asserted directly, because the one thing a probe
    /// must never do is ask this product to resolve a file on disk: that is the act that marks a
    /// file missing and drops the photograph from the library's own index when the disk is not
    /// there, so a health check written as "fetch a thumbnail and see" would delete what it was
    /// checking on.
    /// </summary>
    [Fact]
    public async Task The_library_is_asked_about_itself_and_never_for_a_picture()
    {
        var stub = HealthyPrism("""{"previewToken":"invented0token","version":"260101-invented"}""");

        var health = await Prism(stub).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Reachable);
        health.Version.ShouldBe("260101-invented");
        health.FailureCode.ShouldBeNull();
        health.ProbedAt.ShouldNotBeNull();

        stub.Asked.Count.ShouldBe(3);
        stub.Asked[0].ShouldEndWith(PrismLiveness);
        stub.Asked[1].ShouldEndWith(PrismConfiguration);
        stub.Asked[2].ShouldContain(PrismPositions);
        stub.Asked.ShouldAllBe(url => !url.Contains("/api/v1/t/", StringComparison.Ordinal));

        // One photograph asked for, and a rectangle rather than nothing: the smallest well-formed
        // form of the question this feature actually asks, so a token that answers it is a token
        // the map will work with.
        stub.Asked[2].ShouldContain("count=1");
        stub.Asked[2].ShouldContain("latlng=");
    }

    /// <summary>
    /// The credential the picture route needs arrives in the answer the probe already asked for, so
    /// probing costs the neighbour nothing extra and leaves the first balloon after it free.
    /// </summary>
    [Fact]
    public async Task Probing_takes_the_picture_credential_the_byte_path_would_have_fetched()
    {
        var stub = HealthyPrism().AnsweringPicture("/api/v1/t/", InventedJpeg);

        var library = Prism(stub);
        await library.ProbeAsync(default);

        await using var picture = await library.ThumbnailAsync(
            "aa11bb22cc33", LibraryThumbnailSize.Large, null, default);

        picture.ContentType.ShouldBe("image/jpeg");

        // Four calls, not five: the picture went straight out, because the credential it needs was
        // already in hand from the probe rather than fetched again on the way.
        stub.Asked.Count.ShouldBe(4);
        stub.Asked[3].ShouldContain("invented0token");
    }

    /// <summary>
    /// A version this product writes as a build string rather than as three numbers is carried
    /// through as written. Splitting it into parts would turn a line an operator quotes in a bug
    /// report into a line that quietly says something else.
    /// </summary>
    [Fact]
    public async Task A_library_that_names_no_version_is_reported_without_one_rather_than_with_a_guess()
    {
        var health = await Prism(HealthyPrism()).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Reachable);
        health.Version.ShouldBeNull();
    }

    // --------------------------------------------------------------- a failure is a result, not a throw

    /// <summary>
    /// The contract that makes all of this usable. A library that is not there is the answer the
    /// operator came to read, so producing it must not fail the request they read it in.
    /// </summary>
    [Fact]
    public async Task A_library_that_is_not_there_is_a_result_rather_than_an_exception()
    {
        var health = await Prism(new LibraryStub().NothingListening()).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Unreachable);
        health.FailureCode.ShouldBe(PhotoLibraryException.UnavailableCode);
        health.ProbedAt.ShouldNotBeNull();
    }

    /// <summary>
    /// Answering and refusing is not the same errand as not answering, so it is not the same
    /// answer. One sends somebody to a stopped container and the other to a token that expired —
    /// this product's own default lifetime for one is a year, so it fails silently, once, twelve
    /// months after installation.
    /// </summary>
    /// <remarks>
    /// Written against the route that actually refuses. Measured against a running instance of this
    /// product: the liveness route and the configuration route both answer 200 with no credential
    /// at all and 200 with a wrong one — they hand a picture credential to an anonymous caller — so
    /// a probe asking only those two would report a library whose token expired as healthy. This
    /// case fails against exactly that mistake. The version still comes through, because what an
    /// operator quotes in a bug report is no less useful when the token is wrong.
    /// </remarks>
    [Fact]
    public async Task A_library_that_refuses_the_credential_is_reported_as_answering_and_refusing()
    {
        var stub = new LibraryStub()
            .Answering(PrismLiveness, """{"status":"operational"}""")
            .Answering(PrismConfiguration, """{"previewToken":"invented0token","version":"260101-invented"}""")
            .Refusing(PrismPositions, HttpStatusCode.Unauthorized);

        var health = await Prism(stub).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Reachable);
        health.FailureCode.ShouldBe(PhotoLibraryException.UnauthorizedCode);
        health.Version.ShouldBe("260101-invented");
    }

    /// <summary>
    /// Something that is not this product answering at that address is not this product being
    /// reachable. A page of markup where an answer was expected is what a reverse proxy in front of
    /// the wrong container returns, with a perfectly successful status code.
    /// </summary>
    [Fact]
    public async Task An_address_that_answers_with_something_else_is_not_reported_as_a_working_library()
    {
        var stub = new LibraryStub().AnsweringWithMarkup(PrismLiveness);

        var health = await Prism(stub).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Unreachable);
        health.FailureCode.ShouldBe(PhotoLibraryException.RejectedCode);
    }

    // ------------------------------------------------------- the rights the credential does not carry

    /// <summary>
    /// The set difference, which is the difference between a fix and a support thread. A key here
    /// cannot be narrowed by this application and carries whatever its own account holds, so a key
    /// minted with the wrong rights is the most likely way this integration is misconfigured — and
    /// it fails by drawing an empty map rather than by saying anything.
    /// </summary>
    [Fact]
    public async Task The_rights_the_key_does_not_carry_are_named_one_by_one()
    {
        var stub = new LibraryStub()
            .Answering(ImmichVersion, """{"major":1,"minor":142,"patch":0}""")
            .Answering(ImmichKey, """{"name":"an invented key","permissions":["map.read"]}""");

        var health = await Immich(stub).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Reachable);
        health.Version.ShouldBe("1.142.0");
        health.FailureCode.ShouldBeNull();
        health.MissingPermissions.ShouldBe(["asset.view", "asset.read"]);

        stub.Asked.Count.ShouldBe(2);
        stub.Asked[1].ShouldEndWith(ImmichKey);
    }

    /// <summary>
    /// A key that can do nothing at all still answers this question — the route that describes it
    /// needs no permission of its own — which is exactly the key an operator most needs told about.
    /// </summary>
    [Fact]
    public async Task A_key_that_carries_nothing_is_still_described_and_every_right_is_named()
    {
        var stub = new LibraryStub()
            .Answering(ImmichVersion, """{"major":1,"minor":142,"patch":0}""")
            .Answering(ImmichKey, """{"name":"an invented key","permissions":[]}""");

        var health = await Immich(stub).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Reachable);
        health.MissingPermissions.ShouldBe(["map.read", "asset.view", "asset.read"]);
    }

    /// <summary>
    /// The entry that stands for everything satisfies both. Read as sufficient rather than as a
    /// name this build does not recognise, or every operator who minted a key without narrowing it
    /// — which is the default the library's own screen offers — would be told to add rights
    /// they already have.
    /// </summary>
    [Fact]
    public async Task A_key_minted_without_narrowing_is_missing_nothing()
    {
        var stub = new LibraryStub()
            .Answering(ImmichVersion, """{"major":1,"minor":142,"patch":0}""")
            .Answering(ImmichKey, """{"name":"an invented key","permissions":["all"]}""");

        var health = await Immich(stub).ProbeAsync(default);

        health.MissingPermissions.ShouldBeEmpty();
        health.FailureCode.ShouldBeNull();
    }

    /// <summary>
    /// A build before a release names itself with a suffix beside the three numbers, and reporting
    /// it as the release it precedes is a version line that lies rather than one that says less.
    /// </summary>
    [Fact]
    public async Task A_build_before_a_release_is_not_reported_as_the_release()
    {
        var stub = new LibraryStub()
            .Answering(ImmichVersion, """{"major":3,"minor":2,"patch":0,"prerelease":"rc1"}""")
            .Answering(ImmichKey, """{"name":"an invented key","permissions":["all"]}""");

        (await Immich(stub).ProbeAsync(default)).Version.ShouldBe("3.2.0-rc1");
    }

    [Fact]
    public async Task A_key_carrying_both_rights_and_others_besides_is_missing_nothing()
    {
        var stub = new LibraryStub()
            .Answering(ImmichVersion, """{"major":1,"minor":142,"patch":0}""")
            .Answering(
                ImmichKey,
                """{"name":"an invented key","permissions":["asset.view","album.read","map.read","asset.read"]}""");

        (await Immich(stub).ProbeAsync(default)).MissingPermissions.ShouldBeEmpty();
    }

    /// <summary>
    /// An answer this application cannot read is not evidence either way, and must not be turned
    /// into a claim in either direction: naming rights as missing would send an operator to change
    /// a key that is fine, which is a worse answer than saying nothing about it.
    /// </summary>
    [Fact]
    public async Task An_answer_that_does_not_describe_the_key_names_no_missing_rights()
    {
        var stub = new LibraryStub()
            .Answering(ImmichVersion, """{"major":1,"minor":142,"patch":0}""")
            .Answering(ImmichKey, """{"name":"an invented key"}""");

        var health = await Immich(stub).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Reachable);
        health.MissingPermissions.ShouldBeEmpty();
    }

    /// <summary>
    /// The other library is reported the same way when it is not there: a result, and one that says
    /// which of the two questions failed.
    /// </summary>
    [Fact]
    public async Task The_other_library_being_absent_is_also_a_result_rather_than_an_exception()
    {
        var health = await Immich(new LibraryStub().NothingListening()).ProbeAsync(default);

        health.Reach.ShouldBe(LibraryReach.Unreachable);
        health.FailureCode.ShouldBe(PhotoLibraryException.UnavailableCode);
        health.MissingPermissions.ShouldBeEmpty();
    }

    // ------------------------------------------------------------------------------- the held answer

    /// <summary>
    /// A status line refreshed twice, or read by two people at once, must cost the neighbour one
    /// round of requests rather than one each. The count of calls is the assertion.
    /// </summary>
    [Fact]
    public async Task A_second_look_within_the_window_asks_the_library_nothing()
    {
        var stub = HealthyPrism();
        var library = Prism(stub);

        var first = await library.ProbeAsync(default);
        var second = await library.ProbeAsync(default);

        stub.Asked.Count.ShouldBe(3);

        // The same reading, and it says so: when it was read travels with it, because a health line
        // that describes a minute ago while looking like now is the failure this field prevents.
        second.ProbedAt.ShouldBe(first.ProbedAt);
    }

    /// <summary>
    /// A library that is down is held for the same window as one that is up. Re-asking on every
    /// poll is exactly what one stopped container must not cost, because each ask is a full
    /// timeout.
    /// </summary>
    [Fact]
    public async Task A_library_that_did_not_answer_is_not_asked_again_within_the_window()
    {
        var stub = new LibraryStub().NothingListening();
        var library = Prism(stub);

        await library.ProbeAsync(default);
        var asked = stub.Asked.Count;
        await library.ProbeAsync(default);

        stub.Asked.Count.ShouldBe(asked);
    }

    /// <summary>
    /// The button an operator presses re-asks. A recheck that reopened the picture path and went on
    /// reporting the state read before the fix would be a button that visibly does nothing — and
    /// the operator pressing it has just changed the very thing the line describes.
    /// </summary>
    [Fact]
    public async Task A_recheck_makes_the_next_look_ask_the_library_again()
    {
        var stub = HealthyPrism();
        var library = Prism(stub);

        await library.ProbeAsync(default);
        await library.ProbeAsync(default);
        stub.Asked.Count.ShouldBe(3);

        await library.RecheckOriginalsAsync(default);
        var afterRecheck = stub.Asked.Count;

        await library.ProbeAsync(default);
        stub.Asked.Count.ShouldBeGreaterThan(afterRecheck);
    }

    /// <summary>The same button, on the library that is read whole rather than by rectangle.</summary>
    [Fact]
    public async Task A_recheck_of_the_other_library_makes_the_next_look_ask_it_again()
    {
        var stub = new LibraryStub()
            .Answering(ImmichVersion, """{"major":1,"minor":142,"patch":0}""")
            .Answering(ImmichKey, """{"name":"an invented key","permissions":["all"]}""");
        var library = Immich(stub);

        await library.ProbeAsync(default);
        await library.ProbeAsync(default);
        stub.Asked.Count.ShouldBe(2);

        await library.RecheckOriginalsAsync(default);
        var afterRecheck = stub.Asked.Count;

        await library.ProbeAsync(default);
        stub.Asked.Count.ShouldBeGreaterThan(afterRecheck);
    }

    /// <summary>
    /// The button does its second job even when its first one fails. A recheck against a library
    /// that is still down cannot reopen the picture path — and must still forget what was last
    /// heard, or the operator who has just tried a fix is shown the state from before it for the
    /// rest of the window, which is the button visibly doing nothing at the moment it is pressed
    /// most.
    /// </summary>
    [Fact]
    public async Task A_recheck_that_failed_still_forgets_what_was_last_heard()
    {
        // A picture answer that is not a picture, which is what closes the byte path.
        var stub = HealthyPrism().Answering("/api/v1/t/", """{"error":"no picture here"}""");
        var library = Prism(stub);

        await library.ProbeAsync(default);
        await Should.ThrowAsync<PhotoLibraryException>(async () =>
        {
            await using var refused = await library.ThumbnailAsync(
                "aa11bb22cc33", LibraryThumbnailSize.Large, null, default);
        });
        library.PicturesAvailable.ShouldBeFalse();

        stub.NothingListening();
        await Should.ThrowAsync<PhotoLibraryException>(() => library.RecheckOriginalsAsync(default));

        // Still closed, and that is the half of the button that must not fire on a failure: with
        // the originals out of reach a picture request is a deletion, so the byte path reopens on
        // an answer and on nothing else.
        library.PicturesAvailable.ShouldBeFalse();

        var asked = stub.Asked.Count;
        var reading = await library.ProbeAsync(default);

        stub.Asked.Count.ShouldBeGreaterThan(asked);
        reading.Reach.ShouldBe(LibraryReach.Unreachable);
    }

    /// <summary>The same, on the library that is read whole rather than by rectangle.</summary>
    [Fact]
    public async Task A_failed_recheck_of_the_other_library_also_forgets_what_was_last_heard()
    {
        var stub = new LibraryStub()
            .Answering(ImmichVersion, """{"major":1,"minor":142,"patch":0}""")
            .Answering(ImmichKey, """{"name":"an invented key","permissions":["all"]}""");
        var library = Immich(stub);

        await library.ProbeAsync(default);

        stub.NothingListening();
        await Should.ThrowAsync<PhotoLibraryException>(() => library.RecheckOriginalsAsync(default));

        var asked = stub.Asked.Count;
        var reading = await library.ProbeAsync(default);

        stub.Asked.Count.ShouldBeGreaterThan(asked);
        reading.Reach.ShouldBe(LibraryReach.Unreachable);
    }

    /// <summary>
    /// The de-duplication the permit exists for, and the only load this route was ever at risk of
    /// creating: two readers arriving together, neither finding a reading held, must cost the
    /// neighbour one round of requests rather than two. Written so the second reader provably
    /// arrives while the first is still inside the probe — two sequential looks pass with the
    /// permit deleted, so they prove the window and not the gate.
    /// </summary>
    [Fact]
    public async Task Two_readers_arriving_together_cost_one_round_of_requests()
    {
        var cache = new LibraryHealthCache();
        var inside = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        var probes = 0;

        async Task<LibraryHealth> Probe(CancellationToken ct)
        {
            if (Interlocked.Increment(ref probes) == 1)
            {
                inside.SetResult();
                await release.Task;
            }

            return LibraryHealth.Answered("1.0.0");
        }

        var first = cache.GetAsync(Probe, default);

        // The first reader is now inside the probe holding the permit, and nothing is held yet — so
        // the second reader below can only be waiting on the permit rather than reading an answer.
        await inside.Task;
        var second = cache.GetAsync(Probe, default);
        release.SetResult();

        var readings = await Task.WhenAll(first, second);

        probes.ShouldBe(1);
        readings[1].ProbedAt.ShouldBe(readings[0].ProbedAt);
    }

    // ------------------------------------------------------------------- the window and the deadline

    /// <summary>
    /// The window is short on purpose, and this is the case that pins it: an operator who has just
    /// restarted a container looks at this line to find out whether it worked, so a reading held
    /// for minutes would teach them to distrust the line — which is worse than not drawing one.
    /// </summary>
    [Fact]
    public async Task A_reading_older_than_the_window_is_taken_again()
    {
        var cache = new LibraryHealthCache(TimeSpan.FromMilliseconds(20), LibraryHealthCache.DefaultDeadline);
        var probes = 0;

        Task<LibraryHealth> Probe(CancellationToken ct)
        {
            Interlocked.Increment(ref probes);
            return Task.FromResult(LibraryHealth.Answered("1.0.0"));
        }

        await cache.GetAsync(Probe, default);
        await cache.GetAsync(Probe, default);
        probes.ShouldBe(1);

        await WaitUntilAsync(async () =>
        {
            await cache.GetAsync(Probe, default);
            return probes > 1;
        });
    }

    /// <summary>
    /// A probe that throws for any reason at all is still a result. The never-throw promise is
    /// asserted against the case a product's own client cannot foresee — something other than a
    /// library failure — because that is the one that would otherwise reach a caller.
    /// </summary>
    [Fact]
    public async Task A_probe_that_throws_produces_a_result_rather_than_an_exception()
    {
        var cache = new LibraryHealthCache();

        var health = await cache.GetAsync(
            _ => throw new InvalidOperationException("something nobody wrote a case for"), default);

        health.Reach.ShouldBe(LibraryReach.Unreachable);
        health.FailureCode.ShouldBe(PhotoLibraryException.UnavailableCode);
    }

    /// <summary>
    /// A library that accepts the connection and then says nothing is bounded here rather than by
    /// the timeout an operator configured for reading a viewport: a viewport may legitimately take
    /// twenty seconds, and a health line drawn while somebody waits for it may not.
    /// </summary>
    [Fact]
    public async Task A_library_that_never_answers_is_given_up_on_rather_than_waited_for()
    {
        var cache = new LibraryHealthCache(
            LibraryHealthCache.DefaultWindow, TimeSpan.FromMilliseconds(20));

        var health = await cache.GetAsync(
            async ct =>
            {
                await Task.Delay(Timeout.Infinite, ct);
                return LibraryHealth.Answered("never reached");
            },
            default);

        health.Reach.ShouldBe(LibraryReach.Unreachable);
        health.FailureCode.ShouldBe(PhotoLibraryException.UnavailableCode);
    }

    /// <summary>
    /// The one exception to the never-throw promise, and it is not about the library: a caller that
    /// has gone away has nobody left to tell.
    /// </summary>
    [Fact]
    public async Task A_caller_that_went_away_is_not_answered_at_all()
    {
        var cache = new LibraryHealthCache();
        using var gone = new CancellationTokenSource();
        await gone.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => cache.GetAsync(_ => Task.FromResult(LibraryHealth.Answered("1.0.0")), gone.Token));
    }

    // ------------------------------------------------------------------------------------- support

    /// <summary>A library answering all three of the questions a probe asks it.</summary>
    private static LibraryStub HealthyPrism(string configuration = """{"previewToken":"invented0token"}""") =>
        new LibraryStub()
            .Answering(PrismLiveness, """{"status":"operational"}""")
            .Answering(PrismConfiguration, configuration)
            .Answering(PrismPositions, """{"type":"FeatureCollection","features":[]}""");

    private static PhotoPrismClient Prism(LibraryStub stub, Action<PhotoPrismOptions>? configure = null)
    {
        var options = new PhotoPrismOptions
        {
            Enabled = true,
            BaseUrl = PrismAddress,
            AccessToken = FakeToken,

            // Nothing here is about what a call worth retrying costs, and a retry would make every
            // count of calls below depend on it.
            MaxRetries = 0,
        };
        configure?.Invoke(options);

        return new PhotoPrismClient(
            new OneClient(stub), Options.Create(options), NullLogger<PhotoPrismClient>.Instance);
    }

    private static ImmichClient Immich(LibraryStub stub, Action<ImmichOptions>? configure = null)
    {
        var options = new ImmichOptions
        {
            Enabled = true,
            BaseUrl = ImmichAddress,
            ApiKey = FakeKey,
            MaxRetries = 0,
        };
        configure?.Invoke(options);

        return new ImmichClient(
            new OneClient(stub),
            Options.Create(options),
            new NeverStopping(),
            NullLogger<ImmichClient>.Instance);
    }

    /// <summary>
    /// Waits for something a short window will make true. Polled rather than slept for, so the case
    /// is not written against a clock nobody controls.
    /// </summary>
    private static async Task WaitUntilAsync(Func<Task<bool>> settled)
    {
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < TimeSpan.FromSeconds(10))
        {
            if (await settled())
            {
                return;
            }

            await Task.Delay(10);
        }

        throw new TimeoutException("The reading was never taken again.");
    }

    private static readonly byte[] InventedJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46];

    /// <summary>A factory that hands out one client, over the stub a test supplied.</summary>
    private sealed class OneClient(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>An application that never stops, so nothing detached is cancelled out from under a test.</summary>
    private sealed class NeverStopping : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }

    /// <summary>
    /// Stands in for the library, recording every address that would have left this machine and
    /// answering only the routes a test scripted. An unscripted route is refused rather than
    /// answered, so a probe rewritten to ask a different question is visible in the recorded
    /// addresses rather than absorbed into a healthy-looking answer.
    /// </summary>
    private sealed class LibraryStub : HttpMessageHandler
    {
        private readonly object gate = new();
        private readonly List<string> asked = [];
        private readonly List<(string Route, Func<HttpResponseMessage> Answer)> answers = [];

        private bool nothingListening;

        public IReadOnlyList<string> Asked
        {
            get { lock (gate) { return [.. asked]; } }
        }

        public LibraryStub Answering(string route, string json)
        {
            answers.Add((route, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            }));
            return this;
        }

        /// <summary>What a reverse proxy in front of the wrong container answers, with HTTP 200.</summary>
        public LibraryStub AnsweringWithMarkup(string route)
        {
            answers.Add((route, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("<html>not this product</html>", Encoding.UTF8, "text/html"),
            }));
            return this;
        }

        public LibraryStub AnsweringPicture(string route, byte[] bytes)
        {
            answers.Add((route, () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg") },
                },
            }));
            return this;
        }

        public LibraryStub Refusing(string route, HttpStatusCode status)
        {
            answers.Add((route, () => new HttpResponseMessage(status)
            {
                Content = new StringContent("no", Encoding.UTF8, "text/plain"),
            }));
            return this;
        }

        /// <summary>Nothing at that address at all: a stopped container, or an address nobody serves.</summary>
        public LibraryStub NothingListening()
        {
            nothingListening = true;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var url = request.RequestUri!.ToString();
            lock (gate) { asked.Add(url); }

            if (nothingListening)
            {
                throw new HttpRequestException("nothing is listening at that address");
            }

            foreach (var (route, answer) in answers)
            {
                if (url.Contains(route, StringComparison.Ordinal))
                {
                    return Task.FromResult(answer());
                }
            }

            throw new HttpRequestException($"the stub was not told how to answer {url}");
        }
    }
}
