// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.Extensions.Options;

namespace SilexGis.Api.Features.TripTracking;

/// <summary>
/// The two sizes of a camp's surface log: how long a finished watch stays on it, and how many
/// trips one answer carries.
/// </summary>
/// <remarks>
/// A class and a section of its own rather than two more members of the tracking settings. Those
/// govern what people without an account may read; these govern a screen only signed-in members
/// open, and a single class holding both invites the careless reading that shortening one window
/// shortens the other.
/// </remarks>
public sealed class ExpeditionSurfaceLogOptions
{
    public const string SectionName = "ExpeditionSurfaceLog";

    /// <summary>
    /// The most trips an installation may ask one answer to carry. Each row costs the reading of
    /// that trip's party and of its whole log, on a screen that asks again every half minute, so
    /// the setting has a ceiling as well as a default.
    /// </summary>
    public const int MaxRowsCeiling = 500;

    /// <summary>
    /// How long a trip stays on the log after its watch was closed.
    /// </summary>
    /// <remarks>
    /// Two days by default: long enough that the party that came out last night is still on the
    /// screen at the morning briefing, short enough that a three-week camp does not bury the
    /// parties still underground under every trip it has finished. Zero lists running watches
    /// only. A running watch is listed however old it is — this bounds finished ones.
    /// </remarks>
    public TimeSpan RecentlyClosed { get; set; } = TimeSpan.FromDays(2);

    /// <summary>
    /// The most trips one answer carries. Running watches are taken first and then the most
    /// recently closed, so what a camp over the cap loses is its oldest finished trips; past it
    /// the answer says it was cut short, with a yes or no and never a count.
    /// </summary>
    public int MaxRows { get; set; } = 50;
}

/// <summary>
/// Refuses to start on a surface-log setting that cannot be served.
/// </summary>
/// <remarks>
/// Refused rather than quietly corrected, because both mistakes are silent on the screen they
/// affect. A negative window lists no finished trip at all, and a cap of nothing answers "cut
/// short" over an empty list: each looks, to the coordinator reading it, exactly like a camp with
/// nobody underground — the one misreading this screen exists to prevent. The person who can fix
/// the setting is the one starting the server, so that is who is told.
/// </remarks>
public sealed class ExpeditionSurfaceLogOptionsValidator : IValidateOptions<ExpeditionSurfaceLogOptions>
{
    public ValidateOptionsResult Validate(string? name, ExpeditionSurfaceLogOptions options)
    {
        var failures = new List<string>();

        if (options.RecentlyClosed < TimeSpan.Zero)
        {
            failures.Add(
                $"{ExpeditionSurfaceLogOptions.SectionName}:{nameof(ExpeditionSurfaceLogOptions.RecentlyClosed)} "
                + $"must be zero or longer (zero lists running watches only); got {options.RecentlyClosed:c}.");
        }

        if (options.MaxRows is < 1 or > ExpeditionSurfaceLogOptions.MaxRowsCeiling)
        {
            failures.Add(
                $"{ExpeditionSurfaceLogOptions.SectionName}:{nameof(ExpeditionSurfaceLogOptions.MaxRows)} "
                + $"must be between 1 and {ExpeditionSurfaceLogOptions.MaxRowsCeiling}; got {options.MaxRows}.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
