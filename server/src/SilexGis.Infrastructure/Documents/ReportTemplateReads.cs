// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Trips;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Documents;

/// <summary>
/// Which stored layout a write-up is built in.
/// </summary>
/// <remarks>
/// It lives beside the composition rather than with either kind of write-up, because more than one
/// of them asks the same question — a trip's document and a camp's — and the answer has to be the
/// same question asked once. A surface reaching into another's copy of it is how the two come to
/// resolve a layout differently, and it is also how one part of the application ends up depending
/// on the internals of another.
/// </remarks>
public static class ReportTemplateReads
{
    /// <summary>A layout was named and there is no such layout of that kind.</summary>
    public const string NotFoundCode = "trip_report_template.not_found";

    /// <summary>
    /// The layout a write-up should be built in: the one asked for, the one chosen as default, or
    /// the one the product ships.
    /// </summary>
    /// <remarks>
    /// Returns null when a layout was named and there is no such row, so the caller can refuse
    /// rather than quietly produce a document in a layout nobody asked for.
    /// </remarks>
    public static async Task<string?> BodyForAsync(
        SilexGisDbContext db, Guid? templateId, ReportTemplateKind kind, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);

        // Named, and of the wrong kind, is the same answer as named and absent: a camp layout
        // asked for on a trip would name fields a trip has no answer for, and quietly writing
        // the document under some other layout is the one outcome nobody would be told about.
        if (templateId is { } id)
        {
            return await db.TripReportTemplates.AsNoTracking()
                .Where(x => x.Id == id && x.Kind == kind).Select(x => x.Body).FirstOrDefaultAsync(ct);
        }

        var chosen = await db.TripReportTemplates.AsNoTracking()
            .Where(x => x.IsDefault && x.Kind == kind).Select(x => x.Body).FirstOrDefaultAsync(ct);
        return chosen ?? ReportTemplateFormat.DefaultFor(kind);
    }
}
