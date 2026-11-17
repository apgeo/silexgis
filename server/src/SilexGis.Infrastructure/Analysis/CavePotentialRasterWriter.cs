// SPDX-License-Identifier: AGPL-3.0-or-later
using OSGeo.GDAL;
using OSGeo.OSR;
using SilexGis.Domain.Potential;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Geodata;
using SilexGis.Infrastructure.Terrain;

namespace SilexGis.Infrastructure.Analysis;

/// <summary>
/// Writes the cells of one cave-potential run as a cloud-optimised raster a map can read in
/// ranges.
/// </summary>
/// <remarks>
/// Not safe to call from two threads at once, like everything else here that drives the raster
/// library; the queue that runs terrain work is single-threaded, and this is only ever called
/// from it. The whole of a call is synchronous, so the scratch-directory hold it takes never
/// spans an await.
/// </remarks>
public sealed class CavePotentialRasterWriter
{
    /// <summary>The value a cell with no answer is written as.</summary>
    public const double NoData = -9999d;

    private const string PartialSuffix = ".partial";

    /// <summary>
    /// Writes <paramref name="values"/> — one per cell of <paramref name="grid"/>, row by row from
    /// the north-west, not-a-number where there is no answer — to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="classes">
    /// Whether the values are class numbers rather than measurements. The reduced copies a map
    /// reads when zoomed out are then made by taking the nearest cell, since the mean of two class
    /// numbers is a third class that is nowhere on the ground; measurements are averaged.
    /// </param>
    /// <exception cref="CavePotentialRunException">The raster could not be written whole.</exception>
    public ComputedTerrainRaster Write(string outputPath, EvidenceGrid grid, float[] values, bool classes)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(values);
        GdalDemDerivatives.RequireAbsolute(outputPath, nameof(outputPath));
        if (values.Length != grid.CellCount)
        {
            throw new ArgumentException("One value for every cell of the grid.", nameof(values));
        }

        var directory = Path.GetDirectoryName(outputPath)!;
        Directory.CreateDirectory(directory);
        var partial = outputPath + PartialSuffix;

        var cells = new float[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            cells[i] = float.IsNaN(values[i]) ? (float)NoData : values[i];
        }

        using var scratch = GdalScratchDirectory.At(directory);
        try
        {
            Gdal.ErrorReset();
            using (var memory = Gdal.GetDriverByName("MEM")
                       .Create(string.Empty, grid.Columns, grid.Rows, 1, DataType.GDT_Float32, null))
            {
                memory.SetGeoTransform(
                    [grid.West, grid.CellWidthDegrees, 0d, grid.North, 0d, -grid.CellHeightDegrees]);
                using (var reference = new SpatialReference(string.Empty))
                {
                    reference.ImportFromEPSG(TerrainRasterPreparation.TargetEpsg);
                    reference.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
                    reference.ExportToWkt(out var wkt, null);
                    memory.SetProjection(wkt);
                }

                using (var band = memory.GetRasterBand(1))
                {
                    band.SetNoDataValue(NoData);
                    if (band.WriteRaster(0, 0, grid.Columns, grid.Rows, cells, grid.Columns, grid.Rows, 0, 0)
                        != CPLErr.CE_None)
                    {
                        throw Failure();
                    }
                }

                string[] options =
                [
                    "COMPRESS=DEFLATE",
                    "BIGTIFF=IF_SAFER",
                    "OVERVIEW_RESAMPLING=" + (classes ? "NEAREST" : "AVERAGE"),
                ];
                using var written = Gdal.GetDriverByName("COG")?.CreateCopy(partial, memory, 0, options, null, null);
                if (written is null)
                {
                    throw Failure();
                }
            }

            if (GdalDemDerivatives.Describe(partial) is null)
            {
                throw Failure();
            }

            File.Move(partial, outputPath, overwrite: true);
            return GdalDemDerivatives.Describe(outputPath) ?? throw Failure();
        }
        catch (Exception e) when (e is not CavePotentialRunException and not OperationCanceledException)
        {
            GdalDemDerivatives.Delete(partial);
            throw Failure();
        }
        catch
        {
            GdalDemDerivatives.Delete(partial);
            throw;
        }
    }

    // The library's own words are left out on purpose: they can quote a path off the server.
    private static CavePotentialRunException Failure() =>
        new(CavePotentialRunFailures.Failed, "The map could not be written.");
}
