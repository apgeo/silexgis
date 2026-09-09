// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Entities;

/// <summary>
/// A register outside this installation that also has an identifier for the same cave.
/// </summary>
/// <remarks>
/// A closed, typed vocabulary rather than free text, because the whole value of one of these
/// is that two systems agree what it means: "12345" is worth nothing without knowing whose
/// numbering it is, and a column of loose strings would let the same number stand for two
/// different registers with nothing to tell them apart. Stored as smallint; the values are a
/// schema contract — append only, never renumber.
/// </remarks>
public enum ExternalIdSystem : short
{
    /// <summary>The international community cave database at grottocenter.org.</summary>
    Grottocenter = 1,

    /// <summary>
    /// The national cave cadastre this installation's caves are registered in. Which
    /// cadastre that is depends on where the installation is run, and the number is written
    /// as the cadastre itself writes it.
    /// </summary>
    NationalCadastre = 2,
}

/// <summary>
/// One identifier another register knows this feature by.
/// </summary>
/// <remarks>
/// <para>
/// At most one identifier per feature per register — a cave with two Grottocenter numbers is a
/// contradiction rather than extra information, and letting both sit there means every reader
/// has to decide which one it believes.
/// </para>
/// <para>
/// The value is stored and nothing else is. What a lookup received in order to find the number
/// — names, coordinates, descriptions held by the far end — is deliberately not kept: this
/// application is not a cache of somebody else's register, and a copy of their data taken at
/// an unknown moment is a copy that quietly goes wrong. The identifier is enough to go and ask
/// them again.
/// </para>
/// </remarks>
public class FeatureExternalId : ITimestamped, IAuditable, IAuditChild
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The feature this identifies. The row dies with it.</summary>
    public Guid FeatureId { get; set; }

    /// <summary>Which register the value belongs to.</summary>
    public ExternalIdSystem System { get; set; }

    /// <summary>
    /// The identifier, exactly as the far end writes it. Text rather than a number: a register
    /// that numbers its entries today may letter them tomorrow, and nothing here does
    /// arithmetic on it.
    /// </summary>
    public required string Value { get; set; }

    /// <summary>Who recorded it. Null once the account is gone.</summary>
    public Guid? CreatedBy { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();

    // The trail belongs on the cave, not on a row nobody goes looking for by its own id.
    public string? RootEntityType => nameof(Feature);

    public string? RootEntityId => FeatureId.ToString();
}

/// <summary>
/// The wire vocabulary of <see cref="ExternalIdSystem"/>. Closed, and spelled here once so a
/// route, a response and a stored row cannot come to disagree about what a register is called.
/// </summary>
public static class ExternalIdSystems
{
    /// <summary>grottocenter.org.</summary>
    public const string GrottocenterCode = "grottocenter";

    /// <summary>The national cave cadastre.</summary>
    public const string NationalCadastreCode = "national_cadastre";

    /// <summary>Every code a request may name, in a stable order.</summary>
    public static readonly IReadOnlyList<string> Codes = [GrottocenterCode, NationalCadastreCode];

    /// <summary>The register a request named, or false when it named something else.</summary>
    public static bool TryParse(string? code, out ExternalIdSystem system)
    {
        switch (code)
        {
            case GrottocenterCode:
                system = ExternalIdSystem.Grottocenter;
                return true;
            case NationalCadastreCode:
                system = ExternalIdSystem.NationalCadastre;
                return true;
            default:
                system = default;
                return false;
        }
    }

    /// <summary>The code for a register.</summary>
    public static string Code(ExternalIdSystem system) => system switch
    {
        ExternalIdSystem.Grottocenter => GrottocenterCode,
        ExternalIdSystem.NationalCadastre => NationalCadastreCode,
        _ => throw new ArgumentOutOfRangeException(nameof(system)),
    };
}
