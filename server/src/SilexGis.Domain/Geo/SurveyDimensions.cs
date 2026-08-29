// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Geo;

/// <summary>
/// The one rule that decides whether a passage dimension read out of a compiled survey file is a
/// measurement or is the file saying nothing was measured.
///
/// <para>
/// Both compiled formats say "not measured" with a negative number, and they do not agree on which
/// negative number. One writes -1 metres directly. The other stores centimetres in a signed
/// integer and fills the field with all bits set, which arrives as -0.01 metres after the
/// conversion to metres — so a rule that tests for a particular sentinel value handles one format
/// and silently keeps the other's non-measurement as a real, very small wall distance. The rule
/// that works for both is the sign, and it lives here once so that neither format's reader carries
/// its own copy of it.
/// </para>
///
/// <para>
/// The answer is null, never a substituted default and never zero. A substituted default invents a
/// passage the surveyor never measured; a zero is a genuine measurement — a station hard against
/// the wall — and is not the same statement. Storing either as if it were the other produces
/// widths, heights and volumes that are wrong while looking entirely plausible.
/// </para>
/// </summary>
public static class SurveyDimensions
{
    /// <summary>
    /// The distance to a wall in metres as the file stated it, or null where the file said it was
    /// not measured.
    /// </summary>
    /// <remarks>
    /// A value that is not a finite number is treated as absence too. Neither format is documented
    /// as producing one, so a NaN or an infinity in a dimension field is a file this application
    /// cannot read a measurement out of — and it must not become a stored dimension, because
    /// arithmetic over it poisons every figure computed from the whole cave rather than just the
    /// one station.
    /// </remarks>
    public static double? Measured(double value) =>
        double.IsFinite(value) && value >= 0 ? value : null;

    /// <summary>
    /// Whether a set of four wall distances contains anything at all. A reading in which nothing
    /// was measured is not a reading and is not worth a row; the file states one whenever it has a
    /// field to fill, whether or not the surveyor filled it.
    /// </summary>
    public static bool AnyMeasured(double? left, double? right, double? up, double? down) =>
        left is not null || right is not null || up is not null || down is not null;
}
