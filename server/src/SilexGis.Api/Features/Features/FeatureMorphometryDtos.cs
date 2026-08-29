// SPDX-License-Identifier: AGPL-3.0-or-later
using FluentValidation;

namespace SilexGis.Api.Features.Features;

/// <summary>
/// The measured shape of one drawn outline — the classical closed-depression parameter set.
/// </summary>
/// <remarks>
/// Every length is metres and every area square metres, measured in the installation's working
/// system rather than in stored degrees, which are not a unit of length and vary with latitude.
/// The centroid comes back in the stored system because that is what a map wants; it is the
/// projected centroid converted back, so it is the middle by area and not the middle by longitude.
/// </remarks>
/// <param name="FeatureId">The feature measured.</param>
/// <param name="Name">Its name, so a table needs no second request.</param>
/// <param name="GeometryValid">
/// False when the outline crosses itself. Such a polygon still has an area function that answers —
/// it answers zero — so every derived figure is withheld instead and this flag says why.
/// </param>
/// <param name="AreaM2">Plan area.</param>
/// <param name="PerimeterM">Plan perimeter.</param>
/// <param name="Circularity">4·π·A / P². One for a circle, falling towards zero as the outline
/// lengthens or frets. Null for a degenerate outline with no perimeter.</param>
/// <param name="LongAxisM">The longer side of the smallest-area rectangle containing the outline.</param>
/// <param name="ShortAxisM">The shorter side of that rectangle.</param>
/// <param name="Elongation">Long axis over short axis: one is equidimensional, larger is longer.</param>
/// <param name="LongAxisAzimuthDegrees">
/// Where the long axis lies, degrees clockwise from the working system's grid north, folded into
/// [0, 180). An axis has no direction, so 190 and 10 are the same alignment and reporting either
/// as a bearing would split one population of dolines in two.
/// </param>
/// <param name="CentroidLongitude">Centroid, stored system.</param>
/// <param name="CentroidLatitude">Centroid, stored system.</param>
public sealed record FeatureMorphometryDto(
    Guid FeatureId,
    string? Name,
    bool GeometryValid,
    double? AreaM2,
    double? PerimeterM,
    double? Circularity,
    double? LongAxisM,
    double? ShortAxisM,
    double? Elongation,
    double? LongAxisAzimuthDegrees,
    double? CentroidLongitude,
    double? CentroidLatitude);

/// <summary>
/// Every measurable outline in a piece of country, largest first.
/// </summary>
/// <param name="Rows">
/// The outlines measured. It carries no count of what was left out: a table saying "and six more
/// you may not see" would answer the very question the omission exists to refuse.
/// </param>
public sealed record FeatureMorphometryTableDto(IReadOnlyList<FeatureMorphometryDto> Rows);

/// <summary>What the per-feature route is asked.</summary>
/// <param name="Id">The feature to measure.</param>
public sealed record FeatureMorphometryRequest(Guid Id);

/// <summary>The one rule there is to state about a request carrying a feature id.</summary>
public sealed class FeatureMorphometryRequestValidator : AbstractValidator<FeatureMorphometryRequest>
{
    public FeatureMorphometryRequestValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("A feature id is required.");
    }
}

/// <summary>What the area route is asked: a box, optionally one kind, and how many rows.</summary>
/// <param name="West">Bounding box, stored system.</param>
/// <param name="South">Bounding box, stored system.</param>
/// <param name="East">Bounding box, stored system.</param>
/// <param name="North">Bounding box, stored system.</param>
/// <param name="FeatureTypeId">
/// The kind to measure, or omitted for every outline in the box. Omitting it is offered because
/// the query is the same one, but a box holds karst areas and cave sectors as well as dolines and
/// a table mixing them measures nothing in particular.
/// </param>
/// <param name="Limit">How many rows to return.</param>
public sealed record FeatureMorphometryTableRequest(
    double West,
    double South,
    double East,
    double North,
    long? FeatureTypeId,
    int? Limit);

/// <summary>
/// Bounds on the area request. The box has to be a box, and the row count is capped rather than
/// left open: each surviving row costs a projection, so an uncapped table over a country-sized box
/// is a request that transforms the whole feature table.
/// </summary>
public sealed class FeatureMorphometryTableRequestValidator : AbstractValidator<FeatureMorphometryTableRequest>
{
    public FeatureMorphometryTableRequestValidator()
    {
        RuleFor(x => x.West).InclusiveBetween(-180, 180);
        RuleFor(x => x.East).InclusiveBetween(-180, 180);
        RuleFor(x => x.South).InclusiveBetween(-90, 90);
        RuleFor(x => x.North).InclusiveBetween(-90, 90);
        RuleFor(x => x.East).GreaterThan(x => x.West)
            .WithMessage("The eastern edge must be east of the western one.");
        RuleFor(x => x.North).GreaterThan(x => x.South)
            .WithMessage("The northern edge must be north of the southern one.");
        RuleFor(x => x.FeatureTypeId).GreaterThan(0).When(x => x.FeatureTypeId is not null);
        RuleFor(x => x.Limit)
            .InclusiveBetween(1, FeatureMorphometryLimits.MaxRows)
            .When(x => x.Limit is not null);
    }
}

/// <summary>The bounds the area route is held to, in one place so the validator and the handler
/// cannot drift apart about them.</summary>
public static class FeatureMorphometryLimits
{
    /// <summary>How many outlines a table may hold at most.</summary>
    public const int MaxRows = 500;

    /// <summary>How many it holds when nobody said.</summary>
    public const int DefaultRows = 100;
}
