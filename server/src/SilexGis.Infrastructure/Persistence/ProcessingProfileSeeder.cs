// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain.Entities;
using SilexGis.Domain.Scans;

namespace SilexGis.Infrastructure.Persistence;

/// <summary>
/// Writes the built-in processing profiles at start-up: inserts the missing ones and brings a
/// stored one back to its definition when that changed. Nobody can edit a built-in, so rewriting
/// one loses nobody's work; what it keeps right is an installation upgraded after a built-in's
/// values were tuned.
/// </summary>
public static class ProcessingProfileSeeder
{
    public static async Task SeedAsync(SilexGisDbContext db, CancellationToken ct = default)
    {
        var ids = BuiltInProcessingProfiles.All.Select(p => p.Id).ToList();
        var stored = await db.ProcessingProfiles.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        foreach (var builtIn in BuiltInProcessingProfiles.All)
        {
            if (!stored.TryGetValue(builtIn.Id, out var row))
            {
                db.ProcessingProfiles.Add(new ProcessingProfile
                {
                    Id = builtIn.Id,
                    StepId = builtIn.StepId,
                    Name = builtIn.Name,
                    Description = builtIn.Description,
                    Scope = ProcessingProfileScope.Installation,
                    Values = builtIn.ValuesJson,
                    Origin = ProcessingProfileOrigins.BuiltIn,
                });
                continue;
            }

            if (row.StepId != builtIn.StepId || row.Name != builtIn.Name || row.Description != builtIn.Description
                || row.Scope != ProcessingProfileScope.Installation || row.OwnerUserId is not null
                || row.Origin != ProcessingProfileOrigins.BuiltIn || !SameJson(row.Values, builtIn.ValuesJson))
            {
                row.StepId = builtIn.StepId;
                row.Name = builtIn.Name;
                row.Description = builtIn.Description;
                row.Scope = ProcessingProfileScope.Installation;
                row.OwnerUserId = null;
                row.CavingGroupId = null;
                row.Values = builtIn.ValuesJson;
                row.Origin = ProcessingProfileOrigins.BuiltIn;
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private static bool SameJson(string a, string b)
    {
        using var left = JsonDocument.Parse(a);
        using var right = JsonDocument.Parse(b);
        return JsonElement.DeepEquals(left.RootElement, right.RootElement);
    }
}
