// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Api.Common;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Map;

/// <summary>
/// Decides which elevation model the 3D scene draws its ground from, out of the two places an
/// installation can have named one.
/// </summary>
/// <remarks>
/// <para>
/// The order is stated and it is not a preference: a pyramid named in this installation's
/// configuration wins, and wins without a question being asked of the database. That is the only
/// way terrain got here before builds existed — an operator bakes a pyramid with a command-line
/// tool, puts it somewhere the web server can read it and names it — and an installation running
/// that way must keep drawing exactly what it drew yesterday after this application learns to bake
/// its own. It also means such an installation pays nothing for a feature it does not use.
/// </para>
/// <para>
/// With nothing named, the build somebody chose supplies all three answers together — where its
/// tiles are, what data they were made from, and what its heights are measured from. All three
/// belong to the same pyramid, so all three move at once; taking the address from one place and the
/// correction from another is how a scene ends up drawing correct ground with every cave about
/// forty metres off it.
/// </para>
/// </remarks>
internal static class TerrainSourceResolver
{
    /// <summary>
    /// The elevation model to publish to the client, or nothing when this installation has none.
    /// </summary>
    public static async Task<TerrainSourceDto?> ResolveAsync(
        TerrainOptions options,
        SilexGisDbContext db,
        CancellationToken ct)
    {
        if (options.IsConfigured)
        {
            return new TerrainSourceDto(
                options.ResolvedUrl,
                string.IsNullOrWhiteSpace(options.Attribution) ? null : options.Attribution.Trim(),
                options.SurveyHeightOffsetM);
        }

        // A build with no stamped version has never had its tiles read back and found whole, and
        // nothing may be drawn on that basis: a pyramid with holes in it is drawn as plausible
        // ground at the wrong resolution with no error reported anywhere. The mark cannot normally
        // be taken by such a build, so this is the second lock on the same door rather than the
        // first — and it is the cheap one, since it costs a term in a query that has to run anyway.
        var active = await db.TerrainBuilds
            .AsNoTracking()
            .Where(b => b.IsActive && b.PyramidVersion != null && b.PyramidVersion != "")
            .Select(b => new
            {
                b.Id,
                b.HeightDatum,
                b.GeoidHeightM,
                Credits = db.TerrainBuildSources
                    .Where(s => s.TerrainBuildId == b.Id)
                    .OrderBy(s => s.Id)
                    .Select(s => s.Attribution)
                    .ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (active is null)
        {
            return null;
        }

        return new TerrainSourceDto(
            TerrainPyramid.PublishedUrl(active.Id),
            // Composed the same way, by the same code, as the credit written into the pyramid's own
            // manifest when it was checked. Two spellings of one credit is a licence statement that
            // disagrees with itself depending on where it is read.
            TerrainPyramidCheck.CreditFrom(active.Credits),
            // The correction has exactly one home, and this is a call to it rather than a second
            // copy of the rule. A build's heights and a configured source's heights are corrected
            // by the same sentence or they are eventually corrected differently, and the difference
            // is roughly forty metres with nothing on screen to explain it.
            GeoidOffset.SurveyToSceneOffsetM(active.HeightDatum, active.GeoidHeightM));
    }
}
