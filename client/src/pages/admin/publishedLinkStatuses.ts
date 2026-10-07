// SPDX-License-Identifier: AGPL-3.0-or-later
import type { PublishedLinkStatus } from '../../api/hooks.ts';

/**
 * Every status a published link can have, in the order the server ranks them: the links doing
 * the most first. Written out rather than read off an answer so the counts and the filter are
 * complete — and in one order — before the first answer has arrived, and so the wording kept for
 * each can be checked against this list in both languages.
 */
export const PUBLISHED_LINK_STATUSES = [
  'followable',
  'inGrace',
  'inArchive',
  'withheld',
  'lapsed',
  'revoked',
] as const satisfies readonly PublishedLinkStatus[];
