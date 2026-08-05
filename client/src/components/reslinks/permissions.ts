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
 * Whether this caller may edit or delete a link: its creator, or a full administrator.
 *
 * One home for the rule, because the row menu on a panel and the link's own page have to
 * offer the same controls — an administrator with no control to use is as wrong as a
 * control that earns a refusal.
 */
export function useMayEditResLink(link: Pick<ResLink, 'createdBy'> | null | undefined): boolean {
  const { data: me } = useMe();
  const { data: groups } = useMyPermissionGroups();

  if (!link) {
    return false;
  }
  const mine = Boolean(me && link.createdBy && me.id === link.createdBy);
  return mine || (groups ?? []).some((group) => group.slug === FULL_ADMINISTRATORS);
}
