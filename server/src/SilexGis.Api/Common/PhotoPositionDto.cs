// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Entities;
using SilexGis.Domain.Geo;
using SilexGis.Domain.Import;

namespace SilexGis.Api.Common;

/// <summary>Where a picture is now, and where it came from.</summary>
public sealed record PhotoPositionDto(
    Guid FileId,
    GeoJsonGeometry? Geom,
    PhotoPositionSource PositionSource,
    double? AltitudeMeters,
    double? DirectionDegrees,
    bool DirectionIsMagnetic,
    double? Dop,
    PositionConfidenceBand Confidence);
