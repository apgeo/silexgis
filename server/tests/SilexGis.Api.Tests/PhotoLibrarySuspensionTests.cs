// SPDX-License-Identifier: AGPL-3.0-or-later
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
