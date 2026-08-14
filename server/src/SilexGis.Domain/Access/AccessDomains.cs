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
        // An album is an arrangement of documents and is governed with them: one right over
        // "the club's photographs" should reach the albums made of them, and a second domain
        // would mean writing every rule twice.
        Album => AccessDomain.Documents,
        Expedition => AccessDomain.Expeditions,
        _ => throw new ArgumentException($"No access domain for {entity.GetType().Name}.", nameof(entity)),
    };
}
