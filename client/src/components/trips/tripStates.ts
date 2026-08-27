// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ActivityState } from '../../api/hooks.ts';

/**
 * The lifecycle vocabulary, in the order a thing moves through it.
 *
 * Written out as the wire words rather than derived from anything, so a state added on the server
 * does not silently become an option nobody decided to offer; and written out **once**, because a
 * trip and a camp share one lifecycle and a second copy of the list is a second thing to keep in
 * step. The colours and the labels for these words live beside them — the badge component keys a
 * record by the same union, and the wording is one set of translation keys the camps borrow.
 *
 * Every surface that offers the list to a reader offers all of it. Narrowing it per surface — "the
 * states that mean it is still ahead of you", say — is a judgement about what somebody wants to
 * see, and it belongs where that judgement is made rather than baked into the vocabulary: a
 * cancelled trip is exactly the thing a person on it most needs to find on a list of what is
 * coming up.
 */
export const ACTIVITY_STATES: readonly ActivityState[] = [
  'draft',
  'proposed',
  'planned',
  'confirmed',
  'done',
  'published',
  'cancelled',
  'delayed',
];
