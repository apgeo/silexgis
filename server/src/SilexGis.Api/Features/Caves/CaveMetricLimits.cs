// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Api.Features.Caves;

/// <summary>
/// The largest values a cave's and an entrance's measured numbers may take, in one place so a
/// number added to the record later is bounded the same way.
/// </summary>
/// <remarks>
/// The columns are fixed-scale numerics: lengths, depths, volume and area are ten digits with two
/// after the point, the ramification index six with three. A value the column cannot hold is
/// refused by the database as a numeric overflow, which reached the caller as a server fault and
/// saved nothing; bounded here it is a validation failure naming the field. The ceiling is the
/// column's own largest value, so anything the database could store is still accepted. Altitude
/// is bounded by the earth instead — below the deepest sea floor and above the highest summit,
/// the same range the phone upload holds a fix's altitude to.
/// </remarks>
public static class CaveMetricLimits
{
    /// <summary>The largest magnitude a ten-digit, two-decimal column holds.</summary>
    public const decimal MaxMeasure = 99_999_999.99m;

    /// <summary>The largest magnitude the six-digit, three-decimal ramification index holds.</summary>
    public const decimal MaxRamificationIndex = 999.999m;

    public const decimal MinAltitude = -12_000m;
    public const decimal MaxAltitude = 9_000m;
}
