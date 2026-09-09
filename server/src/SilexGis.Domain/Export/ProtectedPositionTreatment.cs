// SPDX-License-Identifier: AGPL-3.0-or-later
using NetTopologySuite.Geometries;
using SilexGis.Domain.Geo;

namespace SilexGis.Domain.Export;

/// <summary>
/// What an export may be asked to do with the position of a cave this installation
/// protects. Three members and no fourth.
/// </summary>
/// <remarks>
/// The exact position is deliberately not among them, and it is not among them by
/// construction rather than by a check somewhere: there is no member naming it and no code
/// that parses to one, so no request can ask for it and no future caller can reach it by
/// passing a value nobody thought to reject. The reason it is worth spending a type on is
/// that an exported file outlives the permission check that produced it — it is copied,
/// forwarded and kept long after the account that asked for it lost the right — so "an
/// administrator asked for it" is not a safeguard the file carries with it.
/// </remarks>
public enum ProtectedPositionTreatment
{
    /// <summary>
    /// Every non-locating attribute of the cave, and no coordinates at all. The record
    /// still asserts that the cave exists and says what it is.
    /// </summary>
    NoPosition = 0,

    /// <summary>
    /// The cave is left out of the file entirely. The file must still say that some caves
    /// were left out, or counts taken from it disagree with the registry and the recipient
    /// has no way to know.
    /// </summary>
    Omit = 1,

    /// <summary>
    /// The cave is included at the coarse grid position this application already publishes
    /// on its map for callers without exact-location rights — the same snapped coordinate,
    /// from the same rule, so the two surfaces cannot come to disagree about how far the
    /// position was moved.
    /// </summary>
    GridPosition = 2,
}

/// <summary>
/// The wire vocabulary of <see cref="ProtectedPositionTreatment"/> and the one place a
/// treatment turns into a coordinate.
/// </summary>
public static class ProtectedPositionTreatments
{
    /// <summary>Include the cave with no coordinates.</summary>
    public const string NoPositionCode = "no_position";

    /// <summary>Leave the cave out of the file.</summary>
    public const string OmitCode = "omit";

    /// <summary>Include the cave at the protection-grid position.</summary>
    public const string GridPositionCode = "grid_position";

    /// <summary>Every code a request may name, in a stable order for error messages.</summary>
    public static readonly IReadOnlyList<string> Codes = [NoPositionCode, OmitCode, GridPositionCode];

    /// <summary>
    /// The treatment a request named, or false when it named something else. Anything not
    /// in <see cref="Codes"/> fails here — including any spelling of an exact position,
    /// which is the point: the vocabulary is closed, so refusing an unknown code and
    /// refusing an exact position are the same code path and cannot drift apart.
    /// </summary>
    public static bool TryParse(string? code, out ProtectedPositionTreatment treatment)
    {
        switch (code)
        {
            case NoPositionCode:
                treatment = ProtectedPositionTreatment.NoPosition;
                return true;
            case OmitCode:
                treatment = ProtectedPositionTreatment.Omit;
                return true;
            case GridPositionCode:
                treatment = ProtectedPositionTreatment.GridPosition;
                return true;
            default:
                treatment = default;
                return false;
        }
    }

    /// <summary>The code for a treatment — what the file and the audit trail record.</summary>
    public static string Code(ProtectedPositionTreatment treatment) => treatment switch
    {
        ProtectedPositionTreatment.NoPosition => NoPositionCode,
        ProtectedPositionTreatment.Omit => OmitCode,
        ProtectedPositionTreatment.GridPosition => GridPositionCode,
        _ => throw new ArgumentOutOfRangeException(nameof(treatment)),
    };

    /// <summary>
    /// The coordinate a treatment puts in the file, or null when it puts none there.
    /// </summary>
    /// <remarks>
    /// The grid arm calls the application's single snapping rule rather than rounding
    /// here. A second rounding written next to an exporter is how one surface comes to
    /// hide a position by a different distance than another, and the difference between
    /// two obfuscations of the same point narrows down where the point is.
    /// </remarks>
    /// <param name="treatment">What was chosen for this cave.</param>
    /// <param name="exact">The cave's real position. Never emitted; only snapped.</param>
    /// <param name="gridMeters">The installation's obfuscation grid size.</param>
    public static Point? Position(ProtectedPositionTreatment treatment, Point exact, double gridMeters) =>
        treatment == ProtectedPositionTreatment.GridPosition
            ? LocationProtection.Snap(exact, gridMeters)
            : null;
}
