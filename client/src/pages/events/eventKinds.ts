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
