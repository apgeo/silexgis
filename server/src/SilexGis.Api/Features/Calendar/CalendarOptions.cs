// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.Calendar;

/// <summary>The one size the calendar's answer is bounded by.</summary>
public sealed class CalendarOptions
{
    public const string SectionName = "Calendar";

    /// <summary>
    /// Backstop on the rows one answer carries. It is not a page size — the window is what bounds
    /// an ordinary answer — and it is not expected to bite; when it does, the answer says how many
    /// rows it could not carry rather than ending quietly. A setting so an installation can move it
    /// and a test can lower it to where it bites.
    /// </summary>
    public int MaxRows { get; set; } = 2000;
}
