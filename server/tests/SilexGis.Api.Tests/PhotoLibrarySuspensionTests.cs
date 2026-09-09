// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using Shouldly;
using SilexGis.Api.Features.PhotoLibraries;
using SilexGis.Domain.PhotoLibraries;
using SilexGis.Domain.Settings;
using SilexGis.Infrastructure.PhotoLibraries;

namespace SilexGis.Api.Tests;

/// <summary>
/// The brake on a neighbouring photo library: what it stops, what it deliberately cannot do, and
/// the proof that a stopped library is asked nothing at all.
///
/// <para>
/// No database and no host. What is under test is the one decision every route in the slice makes
/// before anything leaves the machine, and the failure it exists to prevent is silent: a library
/// somebody stopped that goes on being asked questions looks exactly like a library that is
/// working, and the only symptom is traffic at a neighbouring container nobody is watching. So the
/// assertions are as much about what was <em>not</em> asked as about what came back.
/// </para>
/// <para>
/// Every library below is a stub. Nothing here reaches a real photo library, and no photograph,
/// address or credential named here exists.
/// </para>
/// </summary>
public sealed class PhotoLibrarySuspensionTests
{
    // ------------------------------------------------------------------ what the brake stops

    [Fact]
    public async Task A_configured_library_that_nobody_has_stopped_is_handed_back()
    {
        var immich = new StubLibrary(PhotoLibrarySource.Immich, configured: true);
        var gate = GateOver([immich], new PhotoLibrarySuspensionSettings());

        (await gate.UsableAsync(PhotoLibrarySource.Immich, CancellationToken.None))
            .ShouldBeSameAs(immich);
    }

    /// <summary>
    /// The whole feature in one case: a stopped library is not handed to a route, so there is no
    /// object to call and no socket to open. Asserted as "nothing was asked of it" rather than as
    /// "the answer was empty", because an empty answer is what a working library gives about an
    /// empty valley and would prove nothing.
    /// </summary>
    [Fact]
    public async Task A_stopped_library_is_not_handed_back_and_nothing_is_asked_of_it()
    {
        var immich = new StubLibrary(PhotoLibrarySource.Immich, configured: true);
        var gate = GateOver([immich], new PhotoLibrarySuspensionSettings { ImmichSuspended = true });

        (await gate.UsableAsync(PhotoLibrarySource.Immich, CancellationToken.None))
            .ShouldBeNull();
        immich.Calls.ShouldBe(0);
    }

    /// <summary>
    /// Per library, because the reason to stop one is rarely a reason to stop the other: a
    /// credential being replaced on one installation says nothing about the other product.
    /// </summary>
    [Fact]
    public async Task Stopping_one_library_leaves_the_other_running()
    {
        var immich = new StubLibrary(PhotoLibrarySource.Immich, configured: true);
        var prism = new StubLibrary(PhotoLibrarySource.PhotoPrism, configured: true);
        var gate = GateOver([immich, prism], new PhotoLibrarySuspensionSettings { ImmichSuspended = true });

        var ct = CancellationToken.None;
        (await gate.UsableAsync(PhotoLibrarySource.Immich, ct)).ShouldBeNull();
        (await gate.UsableAsync(PhotoLibrarySource.PhotoPrism, ct)).ShouldBeSameAs(prism);
    }

    // ------------------------------------------------------- what the brake deliberately cannot do

    /// <summary>
    /// The asymmetry, pinned. Configuration decides whether this installation has a library at all,
    /// and the stored setting can only stop one it already has: an unconfigured library stays
    /// unusable whichever way the switch is left, so there is no arrangement of stored settings
    /// that conjures a library the deployment never supplied.
    /// </summary>
    /// <remarks>
    /// Written as both positions rather than one, because the mistake this guards against is a
    /// later reader "completing" the pair into an enable/disable toggle — and a toggle would pass a
    /// test that only ever set it to true.
    /// </remarks>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_library_nobody_configured_cannot_be_started_from_the_stored_settings(bool suspended)
    {
        var immich = new StubLibrary(PhotoLibrarySource.Immich, configured: false);
        var gate = GateOver([immich], new PhotoLibrarySuspensionSettings { ImmichSuspended = suspended });

        (await gate.UsableAsync(PhotoLibrarySource.Immich, CancellationToken.None))
            .ShouldBeNull();
        immich.Calls.ShouldBe(0);
    }

    /// <summary>
    /// A product this build knows is reported as run by this installation even while it is stopped,
    /// because the map route tells the two apart: a product nobody runs is not found, and a library
    /// this installation is not using answers with nothing in it.
    /// </summary>
    [Fact]
    public void A_stopped_library_is_still_a_product_this_installation_runs()
    {
        var gate = GateOver(
            [new StubLibrary(PhotoLibrarySource.Immich, configured: true)],
            new PhotoLibrarySuspensionSettings { ImmichSuspended = true });

        gate.Runs(PhotoLibrarySource.Immich).ShouldBeTrue();
        gate.Runs(PhotoLibrarySource.PhotoPrism).ShouldBeFalse();
    }

    // ------------------------------------------------------------------ what the status surface says

    /// <summary>
    /// The status surface is the one path that asks every library a question on purpose, so it is
    /// the one most likely to keep talking to a library somebody stopped. It reports the stopped
    /// one as stopped, claims nothing about whether it would have answered, and asks it nothing.
    /// </summary>
    [Fact]
    public async Task The_status_survey_stops_at_a_stopped_library_and_asks_the_other_one()
    {
        var immich = new StubLibrary(PhotoLibrarySource.Immich, configured: true);
        var prism = new StubLibrary(PhotoLibrarySource.PhotoPrism, configured: true);
        var gate = GateOver([immich, prism], new PhotoLibrarySuspensionSettings { ImmichSuspended = true });

        var reports = await gate.SurveyAsync(CancellationToken.None);

        var stopped = reports.Single(report => report.Source == PhotoLibrarySource.Immich);
        stopped.Configured.ShouldBeTrue();
        stopped.Suspended.ShouldBeTrue();
        // "Nobody asked" rather than "did not answer": inventing a failure for a library nothing
        // was said to would send an operator to restart a container that is working.
        stopped.Health.Reach.ShouldBe(LibraryReach.Unknown);
        stopped.Health.ProbedAt.ShouldBeNull();
        // No bytes either. A library nothing may be asked of is not one pictures may be fetched
        // from, whatever its own byte gate last recorded.
        stopped.PicturesAvailable.ShouldBeFalse();
        immich.Calls.ShouldBe(0);

        var running = reports.Single(report => report.Source == PhotoLibrarySource.PhotoPrism);
        running.Suspended.ShouldBeFalse();
        running.Health.Reach.ShouldBe(LibraryReach.Reachable);
        running.PicturesAvailable.ShouldBeTrue();
        prism.Calls.ShouldBe(1);
    }

    /// <summary>
    /// A library nobody configured is reported as unconfigured and never as stopped. The two are
    /// different errands — one is "connect something", the other is "start using what you have" —
    /// and a surface that merged them would tell an operator to un-stop a library that was never
    /// there.
    /// </summary>
    [Fact]
    public async Task A_library_nobody_configured_is_never_reported_as_stopped()
    {
        var gate = GateOver(
            [new StubLibrary(PhotoLibrarySource.Immich, configured: false)],
            new PhotoLibrarySuspensionSettings { ImmichSuspended = true });

        var report = (await gate.SurveyAsync(CancellationToken.None)).Single();

        report.Configured.ShouldBeFalse();
        report.Suspended.ShouldBeFalse();
    }

    // ------------------------------------------------------------------------------- the setting

    /// <summary>Nothing is stopped until somebody stops it.</summary>
    [Fact]
    public void No_library_is_stopped_unless_somebody_says_so()
    {
        var fresh = new PhotoLibrarySuspensionSettings();

        fresh.IsSuspended(PhotoLibrarySource.Immich).ShouldBeFalse();
        fresh.IsSuspended(PhotoLibrarySource.PhotoPrism).ShouldBeFalse();
    }

    /// <summary>
    /// Stopping a library drops what was read from it, rather than only declining to read more.
    /// The distinction is the whole difference between "stop using it" and "forget what it said",
    /// and it matters because one of these products can only give its located photographs whole:
    /// a reading left resident would put every one of their positions back on the map the instant
    /// somebody released the brake, however long afterwards.
    /// </summary>
    [Fact]
    public async Task Stopping_a_library_makes_it_drop_what_it_was_holding()
    {
        var immich = new StubLibrary(PhotoLibrarySource.Immich, configured: true);
        var gate = GateOver([immich], new PhotoLibrarySuspensionSettings { ImmichSuspended = true });

        (await gate.UsableAsync(PhotoLibrarySource.Immich, CancellationToken.None)).ShouldBeNull();

        immich.Forgotten.ShouldBe(1);
        // Forgetting is done here, not asked of the library: no socket is opened to do it.
        immich.Calls.ShouldBe(0);
    }

    /// <summary>
    /// And on the path an administrator's own screen takes: saving the brake re-asks for the status
    /// survey, so a library taken out of use drops its reading then rather than whenever something
    /// next happens to want it.
    /// </summary>
    [Fact]
    public async Task The_status_survey_tells_a_stopped_library_to_drop_what_it_was_holding()
    {
        var immich = new StubLibrary(PhotoLibrarySource.Immich, configured: true);
        var prism = new StubLibrary(PhotoLibrarySource.PhotoPrism, configured: true);
        var gate = GateOver([immich, prism], new PhotoLibrarySuspensionSettings { ImmichSuspended = true });

        await gate.SurveyAsync(CancellationToken.None);

        immich.Forgotten.ShouldBe(1);
        // The one still in use keeps what it holds; forgetting is what stopping means, not what
        // looking at a status line means.
        prism.Forgotten.ShouldBe(0);
    }

    /// <summary>
    /// A product this build knows about but the stored record has no switch for. The record answers
    /// "not stopped" rather than failing, because failing would take down the one surface that
    /// tells "nobody connected one" apart from "did not answer" and from "stopped" — for every
    /// library including the working ones, at exactly the moment somebody is adding a product.
    /// </summary>
    [Fact]
    public void A_product_the_stored_record_has_no_switch_for_is_not_stopped()
    {
        const PhotoLibrarySource unknown = (PhotoLibrarySource)9999;

        new PhotoLibrarySuspensionSettings().IsSuspended(unknown).ShouldBeFalse();
        PhotoLibrarySuspensionSettings.EverythingStopped.IsSuspended(unknown).ShouldBeFalse();
    }

    /// <summary>
    /// What a stored decision that cannot be read has to mean. These values are never a deployment
    /// default — they exist only because somebody opened the settings and stopped something — so an
    /// unreadable row is a decision that was taken and lost, and the only reading of it that cannot
    /// do harm is the one that keeps the application quiet.
    /// </summary>
    [Fact]
    public void An_unreadable_stored_decision_means_everything_is_stopped()
    {
        var lost = PhotoLibrarySuspensionSettings.EverythingStopped;

        lost.IsSuspended(PhotoLibrarySource.Immich).ShouldBeTrue();
        lost.IsSuspended(PhotoLibrarySource.PhotoPrism).ShouldBeTrue();
    }

    /// <summary>
    /// Two registrations of one product is a deployment mistake, and the status route is where an
    /// operator would go to see it. It must therefore survive one rather than answering 500.
    /// </summary>
    [Fact]
    public async Task Two_registrations_of_one_product_do_not_take_the_status_survey_down()
    {
        var gate = GateOver(
            [
                new StubLibrary(PhotoLibrarySource.Immich, configured: true),
                new StubLibrary(PhotoLibrarySource.Immich, configured: true),
            ],
            new PhotoLibrarySuspensionSettings());

        var reports = await gate.SurveyAsync(CancellationToken.None);

        reports.Count.ShouldBe(2);
        reports.ShouldAllBe(report => report.Health.Reach == LibraryReach.Reachable);
    }

    // ------------------------------------------- what the library clients refuse for themselves

    /// <summary>
    /// The guarantee without the good manners. Every route in the slice asks the gate first, but
    /// "asks the gate" is a thing eight handlers do and a ninth could forget — so the products
    /// themselves ask the same question in the one method their outgoing calls all go through, and
    /// a caller that skipped the gate entirely is refused rather than served.
    /// </summary>
    /// <remarks>
    /// Proved by giving each client a way of making an HTTP call that throws if it is ever reached,
    /// so the assertion is "no socket was opened" rather than "the answer was empty". The addresses
    /// and credentials below are invented and point at nothing.
    /// </remarks>
    [Fact]
    public async Task A_caller_that_skipped_the_gate_is_refused_by_the_immich_client_itself()
    {
        var transport = new UnusableTransport();
        var client = new ImmichClient(
            transport,
            Options.Create(new ImmichOptions
            {
                Enabled = true,
                BaseUrl = "http://immich.invented.example/",
                ApiKey = "invented-key",
            }),
            new StubBrake(suspended: true),
            new StubLifetime(),
            NullLogger<ImmichClient>.Instance);

        var refusal = await Should.ThrowAsync<PhotoLibraryException>(
            () => client.PhotosInAsync(new Envelope(0, 1, 0, 1), 10, CancellationToken.None));

        // The same refusal a library nobody configured gives, because that is what a stopped
        // library is to every surface above: absent rather than broken.
        refusal.Code.ShouldBe(PhotoLibraryException.NotConfiguredCode);
        transport.Reached.ShouldBeFalse();
    }

    [Fact]
    public async Task A_caller_that_skipped_the_gate_is_refused_by_the_photoprism_client_itself()
    {
        var transport = new UnusableTransport();
        var client = new PhotoPrismClient(
            transport,
            Options.Create(new PhotoPrismOptions
            {
                Enabled = true,
                BaseUrl = "http://photoprism.invented.example/",
                AccessToken = "invented-token",
            }),
            new StubBrake(suspended: true),
            NullLogger<PhotoPrismClient>.Instance);

        var refusal = await Should.ThrowAsync<PhotoLibraryException>(
            () => client.PhotosInAsync(new Envelope(0, 1, 0, 1), 10, CancellationToken.None));

        refusal.Code.ShouldBe(PhotoLibraryException.NotConfiguredCode);
        transport.Reached.ShouldBeFalse();
    }

    /// <summary>
    /// The health probe is the one call whose contract is that it never throws, so it refuses
    /// differently: it reports that nobody asked. Inventing a failure for a library nothing was
    /// said to would send an operator to restart a container that is working.
    /// </summary>
    [Fact]
    public async Task A_stopped_library_is_not_even_probed_by_its_own_client()
    {
        var transport = new UnusableTransport();
        var client = new ImmichClient(
            transport,
            Options.Create(new ImmichOptions
            {
                Enabled = true,
                BaseUrl = "http://immich.invented.example/",
                ApiKey = "invented-key",
            }),
            new StubBrake(suspended: true),
            new StubLifetime(),
            NullLogger<ImmichClient>.Instance);

        var health = await client.ProbeAsync(CancellationToken.None);

        health.Reach.ShouldBe(LibraryReach.Unknown);
        health.ProbedAt.ShouldBeNull();
        transport.Reached.ShouldBeFalse();
    }

    private static PhotoLibraryGate GateOver(
        IEnumerable<IPhotoLibrary> libraries, PhotoLibrarySuspensionSettings suspension) =>
        new(libraries, new StubSettings(suspension));

    /// <summary>
    /// A photo library that counts what it was asked and refuses to answer anything else. Every
    /// method that would open a socket throws, so a route that reached one it should not have reach
    /// fails loudly rather than quietly succeeding.
    /// </summary>
    private sealed class StubLibrary(PhotoLibrarySource source, bool configured) : IPhotoLibrary
    {
        /// <summary>How many questions were put to this library. The point of most cases here.</summary>
        public int Calls { get; private set; }

        /// <summary>How many times this library was told to drop what it was holding.</summary>
        public int Forgotten { get; private set; }

        public PhotoLibrarySource Source => source;

        public bool IsConfigured => configured;

        public bool PicturesAvailable => true;

        public LibrarySearchMatching SearchMatching => LibrarySearchMatching.Text;

        public Task<LibraryHealth> ProbeAsync(CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(LibraryHealth.Answered("0.0.0-invented"));
        }

        public Task<LibraryPhotoPage> PhotosInAsync(Envelope bounds, int limit, CancellationToken ct) =>
            throw new NotSupportedException("Nothing in these cases asks a library for a viewport.");

        public Task<LibraryPhotoListPage> ListAsync(LibraryPhotoQuery query, CancellationToken ct) =>
            throw new NotSupportedException("Nothing in these cases asks a library for a listing.");

        public Task<LibraryPhotoSearchPage> SearchAsync(LibraryPhotoSearchQuery search, CancellationToken ct) =>
            throw new NotSupportedException("Nothing in these cases asks a library for a search.");

        public Task<LibraryPhotoDetail?> DetailAsync(string photographId, CancellationToken ct) =>
            throw new NotSupportedException("Nothing in these cases asks a library about a photograph.");

        public Task<LibraryThumbnail> ThumbnailAsync(
            string reference, LibraryThumbnailSize size, string? ifNoneMatch, CancellationToken ct) =>
            throw new NotSupportedException("Nothing in these cases asks a library for a picture.");

        public Task RecheckOriginalsAsync(CancellationToken ct) =>
            throw new NotSupportedException("Nothing in these cases rechecks a library.");

        /// <summary>
        /// Counted rather than refused: forgetting opens no socket, and several cases here are
        /// about whether it happened.
        /// </summary>
        public void Forget() => Forgotten++;
    }

    /// <summary>A brake stuck in one position, so a client's own refusal can be put under test.</summary>
    private sealed class StubBrake(bool suspended) : IPhotoLibraryBrake
    {
        public ValueTask<bool> IsSuspendedAsync(PhotoLibrarySource source, CancellationToken ct) =>
            ValueTask.FromResult(suspended);
    }

    /// <summary>
    /// A way of making HTTP calls that cannot make one. Handing a client this is how "no socket was
    /// opened" is asserted rather than assumed: reaching for it at all fails the case.
    /// </summary>
    private sealed class UnusableTransport : IHttpClientFactory
    {
        public bool Reached { get; private set; }

        public HttpClient CreateClient(string name)
        {
            Reached = true;
            throw new NotSupportedException(
                "A library this installation has stopped using was asked something over the network.");
        }
    }

    /// <summary>The host's own lifetime, which none of these cases ends.</summary>
    private sealed class StubLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() =>
            throw new NotSupportedException("Nothing in these cases stops the application.");
    }

    /// <summary>
    /// The stored installation settings, of which exactly one section matters here. The rest throw:
    /// a gate that read the mail server to decide whether to talk to a photo library would be worth
    /// failing over.
    /// </summary>
    private sealed class StubSettings(PhotoLibrarySuspensionSettings suspension) : IAppSettingsService
    {
        public ValueTask<PhotoLibrarySuspensionSettings> GetPhotoLibrarySuspensionAsync(CancellationToken ct = default) =>
            ValueTask.FromResult(suspension);

        public ValueTask<MailSettings> GetMailAsync(CancellationToken ct = default) => throw NotAsked();

        public ValueTask<SmsSettings> GetSmsAsync(CancellationToken ct = default) => throw NotAsked();

        public ValueTask<SecuritySettings> GetSecurityAsync(CancellationToken ct = default) => throw NotAsked();

        public ValueTask<ProtectionSettings> GetProtectionAsync(CancellationToken ct = default) => throw NotAsked();

        public ValueTask<ImportSettings> GetImportAsync(CancellationToken ct = default) => throw NotAsked();

        public ValueTask<InterfaceSettings> GetInterfaceAsync(CancellationToken ct = default) => throw NotAsked();

        public ValueTask<NotificationSettings> GetNotificationsAsync(CancellationToken ct = default) => throw NotAsked();

        public ValueTask<AnnouncementSettings> GetAnnouncementsAsync(CancellationToken ct = default) => throw NotAsked();

        public Task SaveAsync<T>(string section, T value, CancellationToken ct = default)
            where T : class => throw NotAsked();

        private static NotSupportedException NotAsked() =>
            new("Deciding whether to talk to a photo library reads one section and no other.");
    }
}
