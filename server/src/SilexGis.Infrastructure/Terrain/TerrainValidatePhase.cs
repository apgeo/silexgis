// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>
/// The fourth step: refusing to believe a pyramid until its own bytes say it can be drawn.
/// </summary>
/// <remarks>
/// <para>
/// This step exists because every way a pyramid can be wrong is invisible. Damaged tiles, a level
/// advertised and empty, tiles held and advertised nowhere, tiles written one way and served as
/// another — none of them is an error anywhere. The request is answered, the viewer draws a smooth
/// plausible globe or a hillside at the wrong resolution, and no log on either side says a word. So
/// the pyramid is read here, whole, and a build that has not passed every check is not recorded as
/// having produced anything.
/// </para>
/// <para>
/// It also does the two things the tile-maker leaves undone. It replaces the filler credit with the
/// credit of the data this build was actually made from — a scene that shows its terrain's source
/// otherwise shows the words "insert attribution here", which is worse than showing nothing because
/// it is displayed. And it stamps the manifest with a version taken from the tiles themselves, which
/// every tile address then carries: the tile-maker writes the same constant into every pyramid it
/// has ever made, and tiles are cached for a week, so without this a pyramid made again is served
/// out of viewers' own caches with no request reaching the server at all.
/// </para>
/// </remarks>
public sealed class TerrainValidatePhase(SilexGisDbContext db) : ITerrainPhase
{
    /// <summary>How many bad tiles are named before the rest are counted.</summary>
    /// <remarks>
    /// Enough for somebody to go and look at one; short of a list of thousands, which is what a
    /// disk that filled part way through a large pyramid produces and which would not fit in the
    /// bounded column it is stored in anyway.
    /// </remarks>
    private const int NamedExamples = 5;

    /// <summary>What a pyramid says about itself when nothing better is known.</summary>
    private const string PlainDescription = "Elevation model baked for SilexGIS.";

    public TerrainBuildPhase Phase => TerrainBuildPhase.Validate;

    /// <summary>
    /// Never. Checking is the reason the result can be trusted, so it is never skipped.
    /// </summary>
    /// <remarks>
    /// Every other step of this pipeline can look at the disk and say its work is already there.
    /// This one cannot, and the difference is not a matter of cost: a pyramid that was checked and
    /// has since lost half its tiles looks exactly like one that was checked and is fine, and the
    /// question this step answers is precisely that. Running it again is cheap by comparison with
    /// what it protects, and running it again is safe — the version it stamps is taken from the
    /// tiles, so a pyramid that has not changed is stamped with the string it already carries.
    /// </remarks>
    public Task<bool> IsAlreadyDoneAsync(TerrainBuildContext context, CancellationToken ct) =>
        Task.FromResult(false);

    public async Task RunAsync(TerrainBuildContext context, CancellationToken ct)
    {
        await context.ReportAsync(5, "Checking the tiles", ct);

        var tiles = context.Directories.Tiles;
        var report = TerrainPyramidReader.Read(tiles)
            ?? throw new TerrainBuildException(
                TerrainBuildFailures.PyramidUnreadable,
                "This build's tiles have no manifest, so nothing can find one of them. The manifest "
                + "is what says which ground is covered and how deep the detail goes; without it a "
                + "viewer asks for nothing at all and draws bare ground.");

        Refuse(report);

        await context.ReportAsync(60, "Stamping the manifest", ct);

        var credit = TerrainPyramidCheck.CreditFrom(
            await db.TerrainBuildSources
                .AsNoTracking()
                .Where(s => s.TerrainBuildId == context.Build.Id)
                .OrderBy(s => s.Id)
                .Select(s => s.Attribution)
                .ToListAsync(ct));

        var version = TerrainPyramidCheck.VersionFrom(report.Digest);
        Finish(report.Manifest, credit, version);
        Write(Path.Combine(tiles, TerrainPyramid.ManifestFileName), report.Manifest);

        var size = Occupied(context.Directories.Root);
        await TerrainBuildWrites.RecordPyramidAsync(db, context.Build.Id, size, version, ct);

        await context.LogAsync(
            FormattableString.Invariant(
                $"Checked {report.TileCount} tile(s), {Megabytes(report.TileBytes)} MB, all written as meshes; version {version}."),
            ct);

        await context.LogAsync(
            credit is null
                ? "The pyramid carries no credit: nothing this build was made from declared one."
                : $"The pyramid credits {credit}.",
            ct);

        await context.ReportAsync(100, null, ct);
    }

    /// <summary>
    /// Everything a finished pyramid may not be, in the order that answers the operator's question
    /// first.
    /// </summary>
    /// <remarks>
    /// Holes before the way the tiles are written, deliberately: telling somebody how a directory
    /// with pieces missing ought to be served is advice about the wrong problem.
    /// </remarks>
    private static void Refuse(TerrainPyramidReport report)
    {
        if (report.TileCount == 0)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.PyramidEmpty,
                "This build's manifest describes ground it holds no tiles for, so every request for "
                + "that ground is answered with nothing and no error appears anywhere.");
        }

        if (report.Damaged.Count > 0)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.PyramidDamaged,
                FormattableString.Invariant(
                    $"{report.Damaged.Count} of this build's {report.TileCount} tiles are not tiles: ")
                + "truncated, empty, or something else, which is what a run that ran out of disk or "
                + "was stopped while writing leaves behind. Nothing else would notice — the ground "
                + "they cover is quietly drawn from the coarser tiles above them.",
                Examples(report.Damaged));
        }

        if (report.EmptyLevels.Count > 0)
        {
            var levels = string.Join(
                ", ", report.EmptyLevels.Select(l => l.ToString(CultureInfo.InvariantCulture)));

            throw new TerrainBuildException(
                TerrainBuildFailures.PyramidLevelEmpty,
                $"This build says it holds tiles at level(s) {levels} and holds none there, which "
                + "is a run that stopped part way. What would be drawn is the level above, at the "
                + "wrong resolution and with nothing saying so.");
        }

        if (report.Unadvertised.Count > 0)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.PyramidUnadvertised,
                FormattableString.Invariant(
                    $"{report.Unadvertised.Count} of this build's {report.TileCount} tiles are not ")
                + "in any of the areas its manifest says it covers, so nothing will ever ask for "
                + "them: a viewer reads the manifest, sees no ground there, and sends no request. "
                + "The tiles are on disk and the hillside is bare.",
                Examples(report.Unadvertised));
        }

        if (report.MeshCount > 0 && report.CompressedCount > 0)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.PyramidMixedEncoding,
                FormattableString.Invariant(
                    $"{report.MeshCount} of this build's tiles are written plainly and ")
                + FormattableString.Invariant($"{report.CompressedCount} are compressed. ")
                + "Whatever serves them declares one encoding for the whole directory, so half of "
                + "them would be described by bytes they do not contain, and a viewer handed those "
                + "draws nothing and says nothing.");
        }

        if (report.CompressedCount > 0)
        {
            throw new TerrainBuildException(
                TerrainBuildFailures.PyramidCompressed,
                "This build's tiles are compressed, and this installation serves terrain tiles "
                + "exactly as they sit on disk. The tile-maker it was built with writes them "
                + "uncompressed, so this means it now behaves differently and the rule these files "
                + "are served under has to change before any of this can be drawn.");
        }
    }

    /// <summary>
    /// Replaces what the tile-maker left behind with what is true, and stamps the version.
    /// </summary>
    /// <remarks>
    /// The credit given for this build replaces whatever is in the manifest, including a real
    /// looking credit left by an earlier bake into the same directory — otherwise ground remade
    /// from different data keeps the first data's licence statement. The name travels with the
    /// credit rather than being left alone, because a name kept from one bake beside a credit
    /// written for another has the pyramid naming one source and crediting a different one.
    /// </remarks>
    private static void Finish(JsonObject manifest, string? credit, string version)
    {
        if (credit is not null)
        {
            manifest["attribution"] = credit;
            manifest.Remove("name");
        }
        else
        {
            if (TerrainPyramidCheck.IsFiller(Text(manifest, "attribution")))
            {
                manifest.Remove("attribution");
            }

            if (TerrainPyramidCheck.IsFiller(Text(manifest, "name")))
            {
                manifest.Remove("name");
            }
        }

        if (TerrainPyramidCheck.IsFiller(Text(manifest, "description")))
        {
            manifest["description"] = PlainDescription;
        }

        // A pointer to a legend the tile-maker puts in every manifest and this installation does not
        // publish. Left in, it is an address a viewer may follow to nothing.
        manifest.Remove("legend");

        manifest["version"] = version;
    }

    /// <summary>
    /// Writes the manifest back.
    /// </summary>
    /// <remarks>
    /// Under another name and renamed into place, and with no mark on the front and single-character
    /// line endings. A manifest is read by a browser while tiles are being asked for, and half of
    /// one is a pyramid that cannot be read at all — with the previous whole one already gone.
    /// </remarks>
    private static void Write(string path, JsonObject manifest)
    {
        var partial = path + TerrainRasterFiles.PartialSuffix;
        File.WriteAllText(
            partial,
            manifest.ToJsonString() + "\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(partial, path, overwrite: true);
    }

    /// <summary>What this build takes up: the pyramid and everything kept beside it.</summary>
    private static long Occupied(string root)
    {
        if (!Directory.Exists(root))
        {
            return 0;
        }

        var total = 0L;

        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            try
            {
                total += new FileInfo(file).Length;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // A file that vanished between being listed and being measured. The size shown to
                // an operator is not worth failing a checked build over.
            }
        }

        return total;
    }

    private static string? Text(JsonObject manifest, string name) =>
        manifest[name] is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string Examples(IReadOnlyList<string> paths)
    {
        var named = string.Join('\n', paths.Take(NamedExamples));

        return paths.Count <= NamedExamples
            ? named
            : named + FormattableString.Invariant($"\n… and {paths.Count - NamedExamples} more.");
    }

    private static string Megabytes(long bytes) =>
        (bytes / 1024d / 1024d).ToString("F1", CultureInfo.InvariantCulture);
}
