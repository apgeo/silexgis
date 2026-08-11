// SPDX-License-Identifier: AGPL-3.0-or-later
namespace SilexGis.Domain.Access;

/// <summary>
/// Who administers a shelf. A cabinet is not a guarded thing in its own right — it is a
/// place documents are filed — so every question about changing one (renaming it, moving
/// it, filing at it, deleting it, curating an association that is about it) reduces to the
/// same one: may this caller write documents here.
/// </summary>
/// <remarks>
/// One definition because more than one surface asks. Ancestry is part of the answer: a
/// right granted on a parent shelf reaches the shelves filed under it, which is why the
/// facts carry the whole ancestor chain rather than the shelf's own id. A null chain means
/// the root of the tree, where there is no shelf to name and only a domain-wide right
/// answers.
/// </remarks>
public static class CabinetAccessRules
{
    public static bool MayAdminister(AccessContext? ctx, Guid[]? ancestorIds) =>
        AccessEvaluator.Decide(
            ctx,
            AccessDomain.Documents,
            AccessAction.Write,
            ancestorIds is null ? null : new AccessTargetFacts { CabinetIds = ancestorIds }).Allowed;
}
