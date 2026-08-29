// SPDX-License-Identifier: AGPL-3.0-or-later
import type { EventKind } from '../../api/hooks.ts';

/**
 * The kinds an event may be, written out once.
 *
 * Written out rather than derived from anything, because the server's list is a closed
 * vocabulary whose members are a schema contract — a word appearing here that the server does
 * not have is refused by it under its own code, which is the failure this list is checked
 * against rather than something to guard for here.
 */
export const EVENT_KINDS: readonly EventKind[] = [
  'clubMeeting',
  'training',
  'maintenanceDay',
  'gearCheck',
  'conference',
  'deadline',
] as const;

/**
 * The kinds of event people are asked whether they are coming to.
 *
 * A deadline is a date, not a gathering: there is nobody to come to it, so there is nothing to
 * answer, and its responses tab is not drawn. The server holds the same rule and is the one that
 * enforces it — it refuses the whole group under its own code for a kind that takes no answers —
 * so this decides only whether a reader is offered a tab, never whether an answer is allowed.
 *
 * Written as a total map over the vocabulary rather than as a list of the kinds that do, because
 * a list lets a kind added later inherit an answer nobody chose: the new word simply would not be
 * in it, the sign-up sheet would be absent, and nothing would fail. A map that does not name every
 * kind does not compile, so adding one forces the same deliberate yes-or-no the server's own rule
 * asks for. The rule still lives on the server and is enforced there — this decides only whether a
 * reader is offered a tab — but the two vocabularies are now pinned together by the type rather
 * than by somebody remembering to edit both.
 */
const KINDS_TAKING_RESPONSES: Record<EventKind, boolean> = {
  clubMeeting: true,
  training: true,
  maintenanceDay: true,
  gearCheck: true,
  conference: true,
  deadline: false,
};

export function eventKindTakesResponses(kind: EventKind): boolean {
  return KINDS_TAKING_RESPONSES[kind] ?? false;
}
