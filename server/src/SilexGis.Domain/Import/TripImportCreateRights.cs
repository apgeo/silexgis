// SPDX-License-Identifier: AGPL-3.0-or-later
using SilexGis.Domain.Access;

namespace SilexGis.Domain.Import;

/// <summary>
/// What a caller must hold before a trip spreadsheet may be confirmed, in one place.
///
/// <para>
/// One place because the answer is needed twice and the two must agree: the route asks before it
/// fetches the upload, so that a caller who may not do this is told why rather than told about
/// somebody's file, and the commit asks again, because it is the layer that knows what is being
/// bound. Two copies of this list would drift, and the direction of the drift is always the same —
/// the route stops refusing something the commit still does.
/// </para>
/// <para>
/// The list is per domain because the rules are per domain. Recording trips is one right;
/// extending the cave register with places nobody has surveyed is another, and an installation
/// where only archivists add features expresses exactly that by denying Features/Create to
/// everyone else. Growing the vocabulary every trip form shows is a third. The import switches are
/// the only route by which one right could otherwise be spent as another, because the write cores
/// underneath — the feature writer, the taxonomy insert — carry no permission logic of their own.
/// </para>
/// </summary>
public static class TripImportCreateRights
{
    /// <summary>
    /// The refusal this caller meets under these choices, or null if they meet none. The order is
    /// the order the rights are needed in, so the first thing a caller is told about is the one
    /// that stops them recording anything at all.
    /// </summary>
    public static (string Code, string Message)? Refusal(AccessContext? ctx, TripImportOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!CreateRules.MayCreate(ctx, AccessDomain.TripLogs, options.CavingGroupId))
        {
            return (CreateRules.ForbiddenCode, "You may not record trips here.");
        }

        if (options.CavingGroupId is { } tripGroup
            && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.TripLogs, tripGroup))
        {
            return (CavingGroupBindingRules.ForbiddenCode, "You may not organise trips under that group.");
        }

        // The feature switches. Matching against what the installation already holds needs
        // nothing extra — it is a read, and the visibility walk already decided it. Creating is
        // what needs the right, and it is the register's right rather than the trip log's.
        if (options.CreateMissingCaves || options.CreateMissingAreas)
        {
            if (!CreateRules.MayCreate(ctx, AccessDomain.Features, options.CavingGroupId))
            {
                return (CreateRules.ForbiddenCode, "You may not add caves or areas here.");
            }

            if (options.CavingGroupId is { } featureGroup
                && !CavingGroupBindingRules.MayBind(ctx, AccessDomain.Features, featureGroup))
            {
                return (CavingGroupBindingRules.ForbiddenCode,
                    "You may not file caves or areas under that group.");
            }
        }

        // The vocabulary switch. A term added here is permanent — the list is append-only and an
        // undo deliberately leaves it — and it appears on every trip form in the installation, so
        // it is held to the same right as adding one through the vocabulary screen.
        if (options.CreateMissingTripTypes
            && !CreateRules.MayCreate(ctx, AccessDomain.Taxonomies))
        {
            return (CreateRules.ForbiddenCode, "You may not add trip types here.");
        }

        // The roster switch. Adding somebody to the club's roster is its own right, held to the
        // same rule as adding them through the roster screen — and this switch is the one that
        // turns that act into a bulk one, since a sheet of a few thousand rows can name hundreds
        // of people nobody has entered yet. An undo deliberately leaves the people it created,
        // so the entries are permanent in the way a vocabulary term is.
        if (options.CreateMissingCavers
            && !CreateRules.MayCreate(ctx, AccessDomain.Cavers))
        {
            return (CreateRules.ForbiddenCode, "You may not add people to the roster here.");
        }

        return null;
    }
}
