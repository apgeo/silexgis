// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain;

/// <summary>How many rows come before a page of a listing.</summary>
/// <remarks>
/// Worked out in 64 bits and held at the largest whole number: <c>(page - 1) * pageSize</c> in
/// 32 bits wraps for a page number in the millions, and the wrapped offset is negative — the
/// database refuses it, which answered a server fault, and an in-memory list skips nothing, which
/// answered the first page again. Held at the top, the page is simply past the end. Here rather
/// than beside the API's paging so that a listing paged in memory by a service can use it too.
/// </remarks>
public static class PageOffset
{
    public static int Of(int page, int pageSize) =>
        (int)Math.Min((Math.Max(page, 1) - 1L) * Math.Max(pageSize, 0), int.MaxValue);
}
