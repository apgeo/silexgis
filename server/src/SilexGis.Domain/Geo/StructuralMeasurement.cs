// SPDX-License-Identifier: AGPL-3.0-or-later
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SilexGis.Domain.Geo;

/// <summary>A plane's orientation as dip direction and dip, the form a measurement is stored in.</summary>
/// <param name="DipDirectionDegrees">Azimuth the plane dips towards, clockwise from north, in [0, 360).</param>
/// <param name="DipDegrees">Angle of the plane below the horizontal, in [0, 90].</param>
public readonly record struct PlaneOrientation(double DipDirectionDegrees, double DipDegrees);

/// <summary>A line's orientation as trend and plunge — what a plane's pole is plotted by.</summary>
/// <param name="TrendDegrees">Azimuth of the line's downward end, in [0, 360).</param>
/// <param name="PlungeDegrees">Angle of the line below the horizontal, in [0, 90].</param>
public readonly record struct LineOrientation(double TrendDegrees, double PlungeDegrees);

/// <summary>
/// A structural reading — bedding, a joint, a fault plane, cleavage — taken at a point on the
/// surface or underground: what kind of plane it is, and how it lies.
/// </summary>
/// <remarks>
/// <para>
/// Dip direction and dip are the stored, canonical form. Strike and dip by the right-hand rule are
/// accepted on entry and converted here, because two people writing "strike 30, dip 40" mean two
/// different planes unless the convention is fixed, and a convention applied by each form that
/// accepts it would sooner or later be applied twice or not at all. The rule: looking along the
/// strike, the plane dips to the right, so the dip direction is the strike plus ninety degrees.
/// </para>
/// <para>
/// Nothing in a reading's properties locates it — a feature's properties reach every reader who may
/// see the row, including one whose position was withheld — so the bag holds only orientation, type,
/// a note and a date.
/// </para>
/// </remarks>
public static class StructuralMeasurement
{
    public const string TypeKey = "measurement_type";
    public const string DipDirectionKey = "dip_direction";
    public const string DipKey = "dip";
    public const string NoteKey = "note";
    public const string MeasuredOnKey = "measured_on";

    /// <summary>
    /// Accepted on entry only: a strike by the right-hand rule, converted to the dip direction and
    /// never stored.
    /// </summary>
    public const string StrikeKey = "strike";

    /// <summary>
    /// The closed list of plane kinds a reading is of. A code once shipped is never renamed: stored
    /// rows carry it, and a comparison that picks readings of one kind finds them by it.
    /// </summary>
    public static readonly IReadOnlyList<string> Types =
        ["bedding", "joint", FaultType, "cleavage", "fracture", "other"];

    /// <summary>The kind of plane a fault is: the readings that say the rock is broken there.</summary>
    public const string FaultType = "fault";

    private const double FullTurn = 360d;
    private const double QuarterTurn = 90d;

    /// <summary>
    /// An azimuth folded into [0, 360). A value that rounds to a full turn comes back as 0, so
    /// "360" and "0" name one direction.
    /// </summary>
    public static double NormalizeAzimuth(double degrees)
    {
        if (!double.IsFinite(degrees))
        {
            throw new ArgumentOutOfRangeException(nameof(degrees), "an azimuth must be finite.");
        }

        var folded = degrees % FullTurn;
        if (folded < 0)
        {
            folded += FullTurn;
        }

        return folded >= FullTurn - 1e-9 ? 0d : folded;
    }

    /// <summary>The dip direction of a plane given by its strike under the right-hand rule.</summary>
    public static double DipDirectionFromStrike(double strikeDegrees) =>
        NormalizeAzimuth(strikeDegrees + QuarterTurn);

    /// <summary>The right-hand-rule strike of a plane given by its dip direction.</summary>
    public static double StrikeFromDipDirection(double dipDirectionDegrees) =>
        NormalizeAzimuth(dipDirectionDegrees - QuarterTurn);

    /// <summary>
    /// The pole of a plane — the line normal to it, taken pointing down — which is what a
    /// lower-hemisphere stereonet plots: it trends opposite the dip direction and plunges at the
    /// complement of the dip.
    /// </summary>
    public static LineOrientation Pole(PlaneOrientation plane)
    {
        if (!double.IsFinite(plane.DipDegrees) || plane.DipDegrees < 0 || plane.DipDegrees > QuarterTurn)
        {
            throw new ArgumentOutOfRangeException(nameof(plane), "a dip must lie between 0 and 90 degrees.");
        }

        return new LineOrientation(
            NormalizeAzimuth(plane.DipDirectionDegrees + 180d),
            QuarterTurn - plane.DipDegrees);
    }

    /// <summary>
    /// The typed-properties schema the structural-measurement kind is seeded with. The type, dip
    /// direction and dip are required: a reading without them is not a reading.
    /// </summary>
    public static string PropertiesSchemaJson()
    {
        var types = string.Join(",", Types.Select(t => "\"" + t + "\""));
        return "{\"type\":\"object\",\"required\":[\"" + TypeKey + "\",\"" + DipDirectionKey + "\",\"" + DipKey + "\"],"
            + "\"properties\":{"
            + "\"" + TypeKey + "\":{\"type\":\"string\",\"title\":\"Measured plane\",\"enum\":[" + types + "]},"
            + "\"" + DipDirectionKey + "\":{\"type\":\"number\",\"title\":\"Dip direction (°)\",\"minimum\":0,\"exclusiveMaximum\":360},"
            + "\"" + DipKey + "\":{\"type\":\"number\",\"title\":\"Dip (°)\",\"minimum\":0,\"maximum\":90},"
            + "\"" + NoteKey + "\":{\"type\":\"string\",\"title\":\"Note\"},"
            + "\"" + MeasuredOnKey + "\":{\"type\":\"string\",\"title\":\"Measured on\",\"format\":\"date\"}"
            + "}}";
    }

    /// <summary>
    /// Brings a reading's property document to its stored form before it is validated: a
    /// right-hand-rule strike becomes the dip direction, and a dip direction of a full turn
    /// becomes 0. Anything it cannot make sense of is left for the schema to refuse, except a strike
    /// that contradicts a dip direction given beside it, which is refused here because the schema
    /// would never see the strike.
    /// </summary>
    /// <returns>The document to store, or the reasons it cannot be.</returns>
    public static (string? Json, IReadOnlyList<string> Errors) NormalizeProperties(string json)
    {
        JsonObject bag;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject parsed)
            {
                return (json, []); // not an object: the schema says so in its own words
            }

            bag = parsed;
        }
        catch (JsonException)
        {
            return (json, []);
        }

        var changed = false;
        if (bag.TryGetPropertyValue(StrikeKey, out var strikeNode))
        {
            if (!TryNumber(strikeNode, out var strike) || strike < 0 || strike > FullTurn)
            {
                return (null, ["strike must be a number of degrees between 0 and 360."]);
            }

            var fromStrike = DipDirectionFromStrike(strike);
            if (bag.TryGetPropertyValue(DipDirectionKey, out var givenNode) && givenNode is not null)
            {
                if (!TryNumber(givenNode, out var given) || !SameAzimuth(given, fromStrike))
                {
                    return (null,
                        ["strike and dip_direction were both given and describe different planes under the right-hand rule."]);
                }
            }

            bag.Remove(StrikeKey);
            bag[DipDirectionKey] = Round(fromStrike);
            changed = true;
        }

        if (bag.TryGetPropertyValue(DipDirectionKey, out var dipDirectionNode)
            && TryNumber(dipDirectionNode, out var dipDirection)
            && dipDirection >= FullTurn - 1e-9 && dipDirection <= FullTurn)
        {
            bag[DipDirectionKey] = 0d;
            changed = true;
        }

        return (changed ? bag.ToJsonString() : json, []);
    }

    /// <summary>
    /// The stored orientation of a reading, or null when its document does not carry a usable one
    /// (an administrator may have loosened the form, so a stored row is not assumed valid).
    /// </summary>
    public static PlaneOrientation? ReadOrientation(string json)
    {
        try
        {
            if (JsonNode.Parse(json) is not JsonObject bag
                || !bag.TryGetPropertyValue(DipDirectionKey, out var ddNode) || !TryNumber(ddNode, out var dd)
                || !bag.TryGetPropertyValue(DipKey, out var dipNode) || !TryNumber(dipNode, out var dip)
                || dd < 0 || dd > FullTurn || dip < 0 || dip > QuarterTurn)
            {
                return null;
            }

            return new PlaneOrientation(NormalizeAzimuth(dd), dip);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// The kind of plane a stored reading names, or null when it names none from the closed list.
    /// </summary>
    public static string? ReadType(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonObject bag
                && bag.TryGetPropertyValue(TypeKey, out var node)
                && node is JsonValue v
                && v.TryGetValue(out string? type)
                && Types.Contains(type)
                ? type
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryNumber(JsonNode? node, out double value)
    {
        value = 0;
        if (node is JsonValue v && v.GetValueKind() == JsonValueKind.Number && v.TryGetValue(out double d) && double.IsFinite(d))
        {
            value = d;
            return true;
        }

        return false;
    }

    private static bool SameAzimuth(double a, double b)
    {
        var gap = Math.Abs(NormalizeAzimuth(a) - NormalizeAzimuth(b));
        return Math.Min(gap, FullTurn - gap) < 1e-6;
    }

    // Floating error from the addition must not turn a typed "120" into 119.99999999999999.
    private static double Round(double degrees) => NormalizeAzimuth(Math.Round(degrees, 9));
}
