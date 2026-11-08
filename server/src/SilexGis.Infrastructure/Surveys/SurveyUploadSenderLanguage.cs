// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// The language the person who sent an upload reads the application in, which the page opened
/// from its public link starts in. A user row is read here, behind the infrastructure seam, so
/// the feature slices never touch the identity tables themselves.
/// </summary>
public static class SurveyUploadSenderLanguage
{
    /// <summary>The saved language code, or null when the person has none saved.</summary>
    public static Task<string?> OfAsync(SilexGisDbContext db, Guid userId, CancellationToken ct) =>
        db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Locale)
            .FirstOrDefaultAsync(ct);
}
