// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.Sync;

/// <summary>
/// The limits a mobile client is told about before it starts, and the only ones it is
/// entitled to assume. A device that has never talked to this installation asks once and
/// then sizes its own pages and batches to the answer, which is why these are announced
/// rather than left to be discovered by a rejected request in a cave with no signal.
/// </summary>
public sealed class SyncOptions
{
    public const string SectionName = "Sync";

    /// <summary>
    /// The largest page of rows a download may hand back in one response. Sized so a page
    /// stays comfortably inside a mobile connection's patience rather than to any storage
    /// limit; a device that wants fewer asks for fewer.
    /// </summary>
    public int PageSizeMax { get; set; } = 500;

    /// <summary>
    /// The most rows one upload batch may carry. A batch is applied as a unit, so this bounds
    /// how much work a single failed or replayed request costs, not how much a device may
    /// eventually send.
    /// </summary>
    public int UploadRowsMax { get; set; } = 500;

    /// <summary>Announced page ceiling, brought inside a range a client can actually use.</summary>
    public int ResolvedPageSizeMax => Math.Clamp(PageSizeMax, 1, 5000);

    /// <summary>Announced batch ceiling, clamped for the same reason.</summary>
    public int ResolvedUploadRowsMax => Math.Clamp(UploadRowsMax, 1, 5000);
}
