// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Refuses to start on a follow-link lifetime, a closing grace or a period after a lapse that
/// cannot mean anything.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a refused start and not a quiet correction.</b> These periods are read on anonymous
/// routes whose every refusal is the same 404 on purpose, so a wrong one does not fail anywhere
/// an operator would look. The lifetime is counted from the midnight that ends a trip's last
/// day. At zero a link therefore stops at that midnight — with a party that is late still
/// underground — and a link made after the trip is dead as it is handed over; below zero it stops
/// sooner still. Each of those answers "not found", exactly as a mistyped address would, and the
/// person who finds out is a family member looking at an empty page. A container that will not
/// come up, with a line that names the setting, is found by the person who typed it, at the
/// moment they typed it.
/// </para>
/// <para>
/// The period after a lapse is optional, and unset is its ordinary state. Below zero it would stop
/// a link listing the cave's other parties before the link's own trip had ended, which nobody can
/// have meant; zero has a meaning and is let through.
/// </para>
/// <para>
/// <b>What is deliberately not checked here.</b> The size of the list of followed trips is held
/// between its bounds where it is read rather than refused, and that stays: a mistyped size
/// serves a sane list, while a mistyped period serves nothing at all and looks like a dead link.
/// </para>
/// </remarks>
public sealed class TripTrackingOptionsValidator : IValidateOptions<TripTrackingOptions>
{
    public ValidateOptionsResult Validate(string? name, TripTrackingOptions options)
    {
        var failures = new List<string>();

        if (options.ShareLifetime <= TimeSpan.Zero)
        {
            failures.Add(
                $"{Setting(nameof(TripTrackingOptions.ShareLifetime))} must be longer than nothing; got "
                + $"{options.ShareLifetime}. It is how long a follow link goes on working after the last day of "
                + "its trip, written days.hours:minutes:seconds — 14.00:00:00 is two weeks. There is no value "
                + "meaning \"never runs out\": set a long period instead (3650.00:00:00 is ten years). To end "
                + "links with the trip itself, set the shortest period there is, 00:00:01. A single link is "
                + "stopped by revoking it, not by this setting.");
        }

        if (options.ShareGraceAfterClose < TimeSpan.Zero)
        {
            failures.Add(
                $"{Setting(nameof(TripTrackingOptions.ShareGraceAfterClose))} must not be negative; got "
                + $"{options.ShareGraceAfterClose}. It is how long a followed page keeps answering after its "
                + "watch is closed, written days.hours:minutes:seconds. 00:00:00 is allowed and means the "
                + "page stops answering the moment the watch is closed.");
        }

        if (options.SiblingWindowAfterLapse is { } siblings && siblings < TimeSpan.Zero)
        {
            failures.Add(
                $"{Setting(nameof(TripTrackingOptions.SiblingWindowAfterLapse))} must not be negative when it "
                + $"is set; got {siblings}. It is how long after its own trip is over a published link goes on "
                + "listing who is being followed in the same cave now, written days.hours:minutes:seconds — "
                + "90.00:00:00 is ninety days. 00:00:00 is allowed and ends that list with the trip's last day. "
                + "For no limit, leave it unset.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>
    /// The periods of these settings, each with the type it is read as — the ones that can be
    /// written in a way that is no period at all.
    /// </summary>
    private static readonly (string Property, Type ReadAs)[] Periods =
    [
        (nameof(TripTrackingOptions.ShareLifetime), typeof(TimeSpan)),
        (nameof(TripTrackingOptions.ShareGraceAfterClose), typeof(TimeSpan)),
        (nameof(TripTrackingOptions.SiblingWindowAfterLapse), typeof(TimeSpan?)),
        (nameof(TripTrackingOptions.QuietAfter), typeof(TimeSpan)),
    ];

    /// <summary>
    /// Refuses a period that cannot be read as one (<c>3h</c>), naming the setting the way it is
    /// typed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why this is not one more rule in the check above.</b> That check is handed settings
    /// that have already been read. A value that is no period never gets that far: reading it is
    /// what fails, with a message that names a configuration path and a type and says nothing an
    /// operator can act on — and it fails again wherever the settings are next read, which is on
    /// the tracking routes and on every published page. So this runs before the settings are
    /// read, as part of making them, and the start-up check that forces them to be made turns it
    /// into a refused start with every unreadable period named at once.
    /// </para>
    /// <para>
    /// Whether a value can be read is decided by reading it, with the reader the settings
    /// themselves are read by, rather than by a second opinion here about what a period looks
    /// like: the two could only ever disagree. A setting nobody wrote is not looked at.
    /// </para>
    /// </remarks>
    public static void RefuseUnreadablePeriods(IConfiguration configuration)
    {
        var failures = new List<string>();
        foreach (var (property, readAs) in Periods)
        {
            var written = configuration.GetSection($"{TripTrackingOptions.SectionName}:{property}");
            if (written.Value is null) continue;
            try
            {
                _ = written.Get(readAs);
            }
            catch (InvalidOperationException)
            {
                failures.Add(
                    $"{Setting(property)} cannot be read as a period of time; got \"{written.Value}\". It is "
                    + "written days.hours:minutes:seconds — 03:00:00 is three hours, 1.12:00:00 a day and a "
                    + "half. A bare number is read as that many days, and a letter for the unit (3h, 90m) is "
                    + "not understood.");
            }
        }

        if (failures.Count > 0)
        {
            throw new OptionsValidationException(
                Microsoft.Extensions.Options.Options.DefaultName, typeof(TripTrackingOptions), failures);
        }
    }

    private static string Setting(string property) =>
        $"{TripTrackingOptions.SectionName}:{property} (SILEXGIS__{TripTrackingOptions.SectionName}__{property})";
}

/// <summary>
/// Refuses to start on a retention for past trips that would keep nothing.
/// </summary>
/// <remarks>
/// Retention is counted from the midnight that ends a trip's last day. At zero or less a trip is
/// therefore too old by that midnight at the latest — which, with the grace a followed page is
/// given after its watch closes, is before it has entered the archive at all. The archive is then
/// there and empty, indistinguishable from outside from a club that has published nothing.
/// Whoever typed it meant one of two other things, each of which has its own way of being said,
/// and the message names both. The size of the list is held between its bounds where it is read
/// rather than refused, as the setting's own remarks record.
/// </remarks>
public sealed class TripPastTrackOptionsValidator : IValidateOptions<TripPastTrackOptions>
{
    public ValidateOptionsResult Validate(string? name, TripPastTrackOptions options)
    {
        if (options.Retention is { } retention && retention <= TimeSpan.Zero)
        {
            return ValidateOptionsResult.Fail(
                $"{TripPastTrackOptions.SectionName}:{nameof(TripPastTrackOptions.Retention)} "
                + $"(SILEXGIS__{TripPastTrackOptions.SectionName}__{nameof(TripPastTrackOptions.Retention)}) must be "
                + $"longer than nothing when it is set; got {retention}. It is how long a trip stays readable "
                + "as a past trip after its last day, written days.hours:minutes:seconds — 365.00:00:00 is a "
                + "year. To keep past trips without limit, leave it unset. To switch the archive of past trips "
                + $"off, set {TripPastTrackOptions.SectionName}:{nameof(TripPastTrackOptions.Enabled)}=false "
                + $"(SILEXGIS__{TripPastTrackOptions.SectionName}__{nameof(TripPastTrackOptions.Enabled)}=false).");
        }

        return ValidateOptionsResult.Success;
    }
}
