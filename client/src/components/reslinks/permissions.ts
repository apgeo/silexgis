// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMe, useMyPermissionGroups, type ResLink } from '../../api/hooks.ts';

/**
 * The protected permission group whose members the server treats as full administrators.
 * The rank itself is published nowhere, but membership of this group is exactly what the
 * server resolves it from, and the group is protected — it cannot be renamed or deleted
 * out from under this check.
 */
const FULL_ADMINISTRATORS = 'full-administrators';

/**
 * Whether this caller holds the rank the server calls a full administrator.
 *
 * The per-domain capability answer cannot express this: it reports what actions a caller
 * holds in each resource domain, and the relation vocabulary is not a resource domain —
 * it is installation-wide wording that every domain's links read from. Membership of the
 * protected group is the same fact the server resolves the rank from, so this asks the
 * question the server will answer rather than approximating it with a nearby right.
 *
 * Loading reads as "no": the controls this gates all earn a refusal without the rank, and
 * an offer that disappears once the answer arrives is worse than one that arrives late.
 */
export function useIsFullAdmin(): boolean {
  const { data: groups } = useMyPermissionGroups();
  return (groups ?? []).some((group) => group.slug === FULL_ADMINISTRATORS);
}

/**
 * Whether this caller may edit or delete a link, as far as the client can tell: its
 * creator, or a full administrator. One home for what the client decides, because the row
 * menu on a panel and the link's own page have to offer the same controls — an
 * administrator with no control to use is as wrong as a control that earns a refusal.
 *
 * This is deliberately narrower than the server's rule, which also admits whoever may
 * write the link's main member. That arm is a decision over a target whose kind varies —
 * a trip, a document, a shelf, a survey model — and the link payload carries only the
 * creator, so the client cannot compute it and does not guess at it. The consequence is
 * that some callers whose edit the server would accept are shown no control; closing that
 * needs the server to say, on the link itself, whether this caller may edit it.
 */
export function useMayEditResLink(link: Pick<ResLink, 'createdBy'> | null | undefined): boolean {
  const { data: me } = useMe();
  const isFullAdmin = useIsFullAdmin();

  if (!link) {
    return false;
  }
  const mine = Boolean(me && link.createdBy && me.id === link.createdBy);
  return mine || isFullAdmin;
}
