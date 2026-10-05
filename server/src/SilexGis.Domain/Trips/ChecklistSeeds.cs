// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Trips;

/// <summary>
/// The one checklist the product ships: what a party settles before it sets off.
/// </summary>
/// <remarks>
/// Written here rather than in the seeder so the lines have one home. It is an ordinary list the
/// installation's administrator owns, published to every account — a published default is that
/// same shape with a wide audience and nothing else — and it is theirs to adapt: the seeder puts
/// the list back when it is gone, never a line.
/// </remarks>
public static class ChecklistSeeds
{
    public const string DefaultTitle = "Before a trip";

    public const string DefaultDescription =
        "What a party settles before it sets off. Copy it for your own trips, or ask an "
        + "administrator to change it for everybody.";

    public static readonly IReadOnlyList<string> DefaultItems =
    [
        "Permit, or the landowner's permission, obtained",
        "Callout arranged and the alarm time agreed",
        "Gear booked, checked and counted",
        "First-aid kit packed",
        "Key collected",
        "Weather and water levels checked",
        "Transport and seats settled",
    ];
}
