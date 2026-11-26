// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SilexGis.Api.Common;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Infrastructure.Files;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Features.Flybys;

/// <summary>What making a bundle reads with, injected together into each route that makes one.</summary>
public sealed record FlybyBundleServices(
    SilexGisDbContext Db,
    IAccessContextAccessor AccessAccessor,
    FeatureProtection Protection,
    IFileStore FileStore,
    IAccessService Access,
    PlacePointDisclosure PlacePoints,
    PlaceStationDisclosure PlaceStations,
    ThumbnailService Thumbnails,
    ICurrentUser CurrentUser,
    IOptions<MapOptions> MapOptions,
    ILoggerFactory Loggers);

/// <summary>A bundle made, with what went into it, counted as the download's headers count it.</summary>
public sealed record FlybyBundleMade(
    byte[] Zip,
    string FileName,
    int Frames,
    int Walls,
    int Lines,
    int NotDrawn,
    int WallsLeftOut,
    int Markers,
    int MarkersNotDrawn,
    int Photos,
    int UndatedLegs,
    int CavesOnSurveyDate,
    int UndatedCaves);

/// <summary>A bundle, a refusal, or a caller who is not signed in.</summary>
public sealed record FlybyBundleOutcome(FlybyBundleMade? Bundle, ProblemHttpResult? Problem = null, bool Unauthorized = false)
{
    public FlybyBundleOutcome(ProblemHttpResult problem)
        : this(null, problem)
    {
    }

    public static FlybyBundleOutcome NotSignedIn { get; } = new(null, null, true);
}
