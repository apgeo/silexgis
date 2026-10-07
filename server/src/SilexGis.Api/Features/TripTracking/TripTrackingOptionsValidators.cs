// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// Refuses to start on a follow-link lifetime or a closing grace that cannot mean anything.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a refused start and not a quiet correction.</b> Both periods are read on anonymous
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

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
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
