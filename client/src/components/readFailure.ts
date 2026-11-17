// SPDX-License-Identifier: AGPL-3.0-or-later
import { isSettledRefusal } from '../api/client.ts';

/**
 * Whether a page whose own read failed has to give up what it shows. A page that holds nothing
 * has nothing to keep. A page that holds an earlier answer keeps it through a failure that says
 * nothing about the object — TanStack Query keeps the data when a later re-read fails, and a
 * server restart or a gateway error must not swap a loaded page, and whatever is being edited in
 * it, for "not found" — but not through a settled refusal, which means the object really has gone
 * from this reader.
 */
export function readFailureEndsPage(isError: boolean, held: unknown, error: unknown): boolean {
  return isError && (held === undefined || held === null || isSettledRefusal(error));
}
