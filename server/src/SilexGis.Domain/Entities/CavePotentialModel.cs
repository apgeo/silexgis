// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Potential;

namespace SilexGis.Domain.Entities;

/// <summary>
/// A way of weighing ground for where caves may be, named and kept: which evidence counts, how
/// each layer is scored, and how the layers weigh against each other.
/// </summary>
/// <remarks>
/// <para>
/// It describes a calculation and holds no place. Running one picks the area and reads the
/// evidence under the runner's own access, so sharing a model with a caving group, or with
/// everyone signed in, hands its readers a recipe and nothing about where anything is. That is why
/// it carries the ordinary owner, caving-group and visibility trio: the trio decides who may read
/// the recipe, and what a run may read is decided afresh for whoever runs it.
/// </para>
/// <para>
/// It is governed with the saved map views and saved filters, as the same kind of thing — a
/// person's saved workspace — so a club's rules over what its members keep reach its models
/// without a second set of rules to keep in step.
/// </para>
/// </remarks>
public class CavePotentialModel : IProtectedEntity, ITimestamped, IAuditable
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>How the weights are arrived at. Decides how <see cref="Definition"/> is read.</summary>
    public CavePotentialMethod Method { get; set; }

    /// <summary>
    /// The definition as JSON (jsonb). Written only by the server, from a definition it parsed and
    /// validated, so nothing a client sent beside the definition's own members is ever stored.
    /// </summary>
    public string Definition { get; set; } = "{}";

    public Guid OwnerUserId { get; set; }

    public Guid? CavingGroupId { get; set; }

    public Visibility Visibility { get; set; } = Visibility.Private;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string AuditId => Id.ToString();
}
