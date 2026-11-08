// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// The processing profile a person used last for one step, so the next upload offers it again.
/// </summary>
/// <remarks>
/// Written whenever an upload or a rerun names a profile, and nowhere else: it records a use, not
/// a setting. It goes with the profile — a choice of a deleted profile is no choice — and with the
/// account. Typed rather than a key in the free-form interface preferences because the server
/// writes it and answers it.
/// </remarks>
public class ScanProfileChoice
{
    public Guid UserId { get; set; }

    /// <summary>The registered step the profile is for.</summary>
    public required string StepId { get; set; }

    public Guid ProfileId { get; set; }

    public DateTimeOffset ChosenAt { get; set; }
}
