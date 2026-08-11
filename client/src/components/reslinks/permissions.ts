// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMyPermissionGroups, type ResLink } from '../../api/hooks.ts';

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
 * Whether this caller may edit or delete a link: the answer the link itself carries, not a
 * rule restated here. One home for the question, because the row menu on a panel and the
 * link's own page have to offer the same controls — a control that earns a refusal is as
 * wrong as a refusal for somebody the server would have accepted.
 *
 * The rule has three arms — the creator, a full administrator, or whoever may write the
 * link's main member — and two of them the client cannot compute. The last is a decision
 * over a target whose kind varies (a trip, a document, a shelf, a survey model), so the
 * client used to recognise the author alone and showed no control to the co-editor of the
 * very thing the link is about. The server now answers it per link, having taken the same
 * decision the write path takes, and this reads that answer. A missing link reads as "no",
 * as does a link still loading: an offer that disappears once the answer arrives is worse
 * than one that arrives late.
 */
export function mayEditResLink(link: Pick<ResLink, 'mayEdit'> | null | undefined): boolean {
  return link?.mayEdit ?? false;
}
