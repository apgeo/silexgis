// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Terrain;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Infrastructure.Terrain;

/// <summary>One computed picture of the ground, as a reader needs to be told about it.</summary>
/// <param name="Stale">
/// Whether the elevation this was drawn from is still the elevation the installation serves.
/// Answered here rather than left to whatever displays it: a picture drawn from superseded ground
/// is still shown, so the only thing standing between a reader and a shaded relief that disagrees
/// with the heights beneath it is that somebody remembered to say so.
/// </param>
public sealed record TerrainDerivativeLayerView(
    Guid Id,
    Guid TerrainBuildId,
    TerrainDerivative Derivative,
    string Name,
    string Settings,
    TerrainDerivativeStatus Status,
    string? ErrorCode,
    string? Message,
    int Version,
    long SizeBytes,
    bool Stale,
    DateTimeOffset? ComputedAt,
    DateTimeOffset CreatedAt);

/// <summary>Reads the computed pictures of the ground, each with whether it is still current.</summary>
/// <remarks>
/// A reader of its own rather than a query at each caller, because the staleness of a picture is
/// two facts joined — which build it came from, and which build is active — and a caller that
/// fetched only the first would have no way to know it was missing the second. There is nothing to
/// stop such a query returning perfectly correct-looking rows that quietly claim every picture is
/// current.
/// </remarks>
public sealed class TerrainDerivativeCatalogue(SilexGisDbContext db)
{
    /// <summary>Every computed picture of one build.</summary>
    public async Task<IReadOnlyList<TerrainDerivativeLayerView>> ForBuildAsync(
        Guid buildId, CancellationToken ct)
    {
        var active = await ActiveBuildIdAsync(ct);
        var rows = await db.TerrainDerivativeLayers
            .AsNoTracking()
            .Where(l => l.TerrainBuildId == buildId)
            .OrderBy(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .ToListAsync(ct);

        return [.. rows.Select(row => View(row, active))];
    }

    /// <summary>Every computed picture there is, newest build first.</summary>
    public async Task<IReadOnlyList<TerrainDerivativeLayerView>> AllAsync(CancellationToken ct)
    {
        var active = await ActiveBuildIdAsync(ct);
        var rows = await db.TerrainDerivativeLayers
            .AsNoTracking()
            .OrderByDescending(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .ToListAsync(ct);

        return [.. rows.Select(row => View(row, active))];
    }

    /// <summary>One computed picture, or nothing if there is no such row.</summary>
    public async Task<TerrainDerivativeLayerView?> FindAsync(Guid id, CancellationToken ct)
    {
        var row = await db.TerrainDerivativeLayers.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct);

        return row is null ? null : View(row, await ActiveBuildIdAsync(ct));
    }

    private async Task<Guid?> ActiveBuildIdAsync(CancellationToken ct)
    {
        var active = await db.TerrainBuilds.AsNoTracking()
            .Where(b => b.IsActive)
            .Select(b => (Guid?)b.Id)
            .FirstOrDefaultAsync(ct);

        return active;
    }

    private static TerrainDerivativeLayerView View(TerrainDerivativeLayer row, Guid? activeBuildId) =>
        new(
            row.Id,
            row.TerrainBuildId,
            row.Derivative,
            row.Name,
            row.Settings,
            row.Status,
            row.ErrorCode,
            row.Message,
            row.Version,
            row.SizeBytes,
            TerrainDerivativeRegistry.IsStale(row.TerrainBuildId, activeBuildId),
            row.ComputedAt,
            row.CreatedAt);
}
