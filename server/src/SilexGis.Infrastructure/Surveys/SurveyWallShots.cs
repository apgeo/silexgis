// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Surveys;
using Therion.Blender;

namespace SilexGis.Infrastructure.Surveys;

/// <summary>
/// Which of a compiled survey's legs are shots at the passage wall rather than passage, worked out
/// from the one thing the file always says: whether the leg ends at a point the survey declined to
/// name.
///
/// <para>
/// <b>Why this is not simply read off the flag.</b> The format has a flag meaning "wall shot", and
/// where an exporter sets it everything downstream already behaves. Measured over eighteen compiled
/// files, six of them — the public demo survey of the viewer this application embeds among them —
/// set that flag on no leg at all while carrying between a hundred and eighty-four thousand legs
/// fired at unnamed points. On those files every consumer that asks the flag is answered "none of
/// these are wall shots", and the consequences are not subtle: the published passage length of the
/// demo survey would be 67.8 km for a cave with 31.7 km of surveyed passage, one measured file 88.2
/// km for 2.1 km, and the morphometrics computed beside it would count every wall point as a dead
/// end of the cave.
/// </para>
///
/// <para>
/// <b>What is done about it, and what is deliberately not.</b> The leg's typed flag gains the wall
/// shot bit; the file's own bits in <see cref="CaveShot.RawFlags"/> are left exactly as the exporter
/// wrote them. That split is what the two flag sets are for — one is this application's reading of
/// the file, the other is the file. Nothing here guesses from geometry or from the shape of the
/// network: the far end of the leg is a station record whose name is the survey language's own
/// token for "there is no station here", so this is reading what the file says, in the file's own
/// vocabulary, and not a statistic about how the surveyor worked.
/// </para>
///
/// <para>
/// Applied once and then harmless: a file whose exporter did set the flag already has the bit, and
/// a file with no unnamed points at all is returned untouched rather than rebuilt.
/// </para>
/// </summary>
public static class SurveyWallShots
{
    /// <summary>
    /// The file ids of the station records that are not stations at all but the survey language's
    /// placeholder for the far end of a wall shot. Empty for a format that has no such token, and
    /// for a survey that fired no wall shots.
    /// </summary>
    public static HashSet<uint> AnonymousPointIds(CaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var ids = new HashSet<uint>();
        foreach (var station in model.Stations)
        {
            if (SurveyStationNames.IsAnonymousPoint(station.Name))
            {
                ids.Add(station.Id);
            }
        }

        return ids;
    }

    /// <summary>
    /// <paramref name="model"/> with every leg that reaches an unnamed point marked as the wall shot
    /// it is, or the very same instance where there is nothing to mark.
    ///
    /// <para>
    /// Whole-model rather than a filtered list of legs, because the consumer this exists for takes a
    /// parsed model and nothing smaller — the graph the cave's morphometrics are computed over is
    /// built from one, and it decides what is passage by asking each leg's flags.
    /// </para>
    /// </summary>
    public static CaveModel Flagged(CaveModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var anonymous = AnonymousPointIds(model);
        if (anonymous.Count == 0)
        {
            return model;
        }

        var shots = new List<CaveShot>(model.Shots.Count);
        var marked = 0;
        foreach (var shot in model.Shots)
        {
            if (ReachesAnonymousPoint(shot, anonymous) && (shot.Flags & CaveShotFlags.Splay) == 0)
            {
                shots.Add(shot with { Flags = shot.Flags | CaveShotFlags.Splay });
                marked++;
            }
            else
            {
                shots.Add(shot);
            }
        }

        // Nothing to say that the file did not already say — an exporter that flags its own wall
        // shots leaves nothing here to do. Answering with the model itself matters because this runs
        // twice on the way through one reading, by design: the extraction settles it for every
        // caller of its own, and the job settles it again for the graph the morphometrics are built
        // from. The second pass over a ninety-thousand-leg export should not copy it.
        if (marked == 0)
        {
            return model;
        }

        // Copied a property at a time, the way the reader's own recentring stage copies it. What
        // changes is the legs; everything else — the survey tree, the wall meshes, the terrain, the
        // separator the labels are cut on — travels through unchanged, and losing any of it here
        // would be losing it from the model the rest of the reading works on.
        return new CaveModel
        {
            SourcePath = model.SourcePath,
            SourceFormat = model.SourceFormat,
            FormatVersion = model.FormatVersion,
            Title = model.Title,
            CoordinateSystem = model.CoordinateSystem,
            SeparatorChar = model.SeparatorChar,
            Datestamp = model.Datestamp,
            Timestamp = model.Timestamp,
            IsExtendedElevation = model.IsExtendedElevation,
            Surveys = model.Surveys,
            Stations = model.Stations,
            Shots = shots,
            Scraps = model.Scraps,
            Surfaces = model.Surfaces,
            SurfaceBitmaps = model.SurfaceBitmaps,
            Passages = model.Passages,
            TraverseErrors = model.TraverseErrors,
        };
    }

    /// <summary>
    /// Whether either end of <paramref name="shot"/> is one of the unnamed points.
    ///
    /// <para>
    /// By the id the file wrote, not by position. A wall shot routinely ends within rounding of a
    /// real station, and matching on position would call an ordinary leg a wall shot for standing
    /// too close to one. The format that has no station ids on its legs has no such token either,
    /// so the set is empty there and this never fires.
    /// </para>
    /// </summary>
    public static bool ReachesAnonymousPoint(CaveShot shot, IReadOnlySet<uint> anonymousPointIds)
    {
        ArgumentNullException.ThrowIfNull(shot);
        ArgumentNullException.ThrowIfNull(anonymousPointIds);

        return (shot.FromStationId is { } from && anonymousPointIds.Contains(from))
            || (shot.ToStationId is { } to && anonymousPointIds.Contains(to));
    }
}
