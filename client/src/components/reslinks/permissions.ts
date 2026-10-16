// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCapabilities, type ResLink } from '../../api/hooks.ts';

/**
 * Whether this caller holds the rank the server calls a full administrator.
 *
 * Read from the flag the capabilities answer states for exactly this question — the one home
 * every screen asks. It used to be inferred here from membership of the protected
 * full-administrators group while other screens read the flag, two answers to one permission
 * question that were free to drift apart; the flag is the server's own resolution of that same
 * membership, stated rather than guessed.
 *
 * Loading reads as "no": the controls this gates all earn a refusal without the rank, and
 * an offer that disappears once the answer arrives is worse than one that arrives late.
 */
export function useIsFullAdmin(): boolean {
  const { data: capabilities } = useCapabilities();
  return capabilities?.isFullAdmin === true;
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
