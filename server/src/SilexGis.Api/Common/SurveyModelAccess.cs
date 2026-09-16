// SPDX-License-Identifier: AGPL-3.0-or-later
using Microsoft.EntityFrameworkCore;
using SilexGis.Domain;
using SilexGis.Domain.Access;
using SilexGis.Domain.Entities;
using SilexGis.Infrastructure.Permissions;
using SilexGis.Infrastructure.Persistence;

namespace SilexGis.Api.Common;

/// <summary>
/// Who may see and who may change a survey model, decided once for every surface that asks.
/// </summary>
/// <remarks>
/// A survey model carries no rights of its own: it belongs to a cave, and the cave answers
/// for it. Two questions, kept apart because the surfaces answer them with different status
/// codes — an invisible model is "not found", a visible one the caller may not change is
/// "forbidden" — but each with a single definition, so a surface that reaches models
/// sideways (through a resource link, an export, a roll-up) cannot end up admitting people
/// the model's own endpoints refuse.
/// </remarks>
public static class SurveyModelAccess
{
    /// <summary>
    /// Whether the caller may see this cave's survey models at all: Read on the cave, and
    /// the cave's exact location open to them. The second half is not decoration — a model
    /// is a measured drawing of a passage, so serving one to a caller without exact-location
    /// view would hand over the position the protection exists to withhold. Metadata is
    /// closed with the file for the same reason.
    /// </summary>
    public static async Task<bool> VisibleAsync(
        IAccessService access,
        FeatureProtection protection,
        AccessContext? ctx,
        Feature cave,
        CancellationToken ct) =>
        (await access.DecideAsync(ctx, AccessAction.Read, cave, ct)).Allowed
        && await LocationOpenAsync(protection, ctx, cave.Id, ct);

    /// <summary>
    /// The exact-location half on its own, for the one surface that needs to tell "you may
    /// not see this cave" apart from "you may see the cave but not its models".
    /// </summary>
    public static async Task<bool> LocationOpenAsync(
        FeatureProtection protection, AccessContext? ctx, Guid caveFeatureId, CancellationToken ct) =>
        (await protection.ExactViewIdsAsync(ctx, [caveFeatureId], ct)).Contains(caveFeatureId);

    /// <summary>
    /// The cave whose survey may be measured, or null when it may not be.
    ///
    /// <para>
    /// Null covers three different situations on purpose: the cave does not exist, the caller may
    /// not read it, and the caller may read it but not place it exactly. Telling them apart would
    /// say which caves are being kept from whom — a refusal that distinguishes "no such cave" from
    /// "not for you" answers the question the protection exists to decline. Every surface that
    /// derives a figure from a cave's survey asks this, and asks it here rather than spelling it
    /// again: a gate whose whole property is that its refusals are indistinguishable cannot have
    /// one copy per route, because the copy that gains a nuance the others lack becomes the
    /// difference a caller can measure.
    /// </para>
    /// </summary>
    /// <param name="withDeclaredFigures">
    /// Load the cave subtype as well, for a caller that sets what the survey measures beside what
    /// the record claims. Off by default: most callers want only the gate, and every one of them
    /// would otherwise pay for a join it never reads.
    /// </param>
    public static async Task<Feature?> MeasurableCaveAsync(
        SilexGisDbContext db,
        IAccessService access,
        FeatureProtection protection,
        AccessContext ctx,
        Guid id,
        CancellationToken ct,
        bool withDeclaredFigures = false)
    {
        var caves = db.Features.AsNoTracking();
        if (withDeclaredFigures)
        {
            caves = caves.Include(f => f.Cave);
        }

        var cave = await caves.FirstOrDefaultAsync(f => f.Id == id && f.Kind == FeatureKind.Cave, ct);

        return cave is not null && await VisibleAsync(access, protection, ctx, cave, ct)
            ? cave
            : null;
    }

    /// <summary>Write on the cave — asked only of a caller who already passes
    /// <see cref="VisibleAsync"/>, never on its own.</summary>
    public static async Task<bool> WritableAsync(
        IAccessService access, AccessContext? ctx, Feature cave, CancellationToken ct) =>
        (await access.DecideAsync(ctx, AccessAction.Write, cave, ct)).Allowed;

    /// <summary>Both halves at once, for a caller that has one answer to give rather than
    /// two status codes to choose between.</summary>
    public static async Task<bool> MayWriteAsync(
        IAccessService access,
        FeatureProtection protection,
        AccessContext? ctx,
        Feature cave,
        CancellationToken ct) =>
        await VisibleAsync(access, protection, ctx, cave, ct)
        && await WritableAsync(access, ctx, cave, ct);
}
