// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// How much a satellite fix is worth, as a band a person can read.
///
/// <para>
/// The bands are the standard reading of dilution of precision, which is the number a receiver
/// actually reports. It is dimensionless — it says how much the satellite geometry multiplied
/// whatever error the receiver had, not how many metres out the fix is — so it is banded rather
/// than converted. Turning it into metres would take a guess at the receiver's own error and
/// produce something that reads as a measurement.
/// </para>
/// </summary>
public enum PositionConfidenceBand
{
    /// <summary>The fix reported nothing about its own quality.</summary>
    Unknown = 0,

    /// <summary>Open sky, many satellites. As good as a handheld gets.</summary>
    Excellent = 1,

    Good = 2,

    /// <summary>Usable, and worth checking against something else before walking to it.</summary>
    Moderate = 3,

    /// <summary>Under a cliff or in a forest: the fix is tens of metres out and should look it.</summary>
    Poor = 4,
}

/// <summary>The single home of how a reported dilution of precision is read.</summary>
public static class PositionConfidence
{
    /// <summary>
    /// The band a dilution of precision falls in. The thresholds are the conventional ones, and
    /// they live here rather than in a screen so that a map, an export and a photograph's panel
    /// cannot disagree about whether the same fix was any good.
    /// </summary>
    public static PositionConfidenceBand Of(double? dop) => dop switch
    {
        null or <= 0 => PositionConfidenceBand.Unknown,
        // Values this large are a damaged tag rather than a fix anybody took.
        > 1000 => PositionConfidenceBand.Unknown,
        < 2 => PositionConfidenceBand.Excellent,
        < 5 => PositionConfidenceBand.Good,
        < 10 => PositionConfidenceBand.Moderate,
        _ => PositionConfidenceBand.Poor,
    };
}
