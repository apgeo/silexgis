// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;

namespace SilexGis.Domain.Access;

/// <summary>Maps a protected entity to the resource domain that governs it.</summary>
public static class AccessDomains
{
    public static AccessDomain Of(IProtectedEntity entity) => entity switch
    {
        Feature => AccessDomain.Features,
        TripLog => AccessDomain.TripLogs,
        Geofile => AccessDomain.Geofiles,
        GeoreferencedMap => AccessDomain.GeoreferencedMaps,
        MapView => AccessDomain.MapViews,
        Document => AccessDomain.Documents,
        _ => throw new ArgumentException($"No access domain for {entity.GetType().Name}.", nameof(entity)),
    };
}
