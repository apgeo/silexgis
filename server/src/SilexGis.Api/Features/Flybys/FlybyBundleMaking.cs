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

/// <summary>Who a bundle is made for.</summary>
public enum FlybyBundleRecipient
{
    /// <summary>The account asking, who downloads it and renders it on a machine of their own.</summary>
    Asker,

    /// <summary>
    /// A render node: another machine, which renders every account's bundles and holds no grant to
    /// place a protected cave. Such a bundle holds nothing under location protection.
    /// </summary>
    /// <remarks>
    /// A node an administrator lets receive protected caves would be a third recipient, decided by
    /// the node and not the asker: such a bundle would be made as <see cref="Asker"/>'s, its render
    /// marked as holding protected caves, and claimed only by such a node. No node is that today.
    /// </remarks>
    RenderNode,
}

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
    int UndatedCaves)
{
    /// <summary>For a render node: the caves under location protection left out.</summary>
    public int ProtectedLeftOut { get; init; }

    /// <summary>For a render node: the entrances and features under location protection left out.</summary>
    public int ProtectedMarkersLeftOut { get; init; }
}

/// <summary>A bundle, a refusal, or a caller who is not signed in.</summary>
public sealed record FlybyBundleOutcome(FlybyBundleMade? Bundle, ProblemHttpResult? Problem = null, bool Unauthorized = false)
{
    public FlybyBundleOutcome(ProblemHttpResult problem)
        : this(null, problem)
    {
    }

    public static FlybyBundleOutcome NotSignedIn { get; } = new(null, null, true);
}
