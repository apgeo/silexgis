// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';

/**
 * The refusals the event surfaces have words of their own for, by the stable code the server
 * sends.
 *
 * One table rather than one per surface. The acts that reach a whole run are offered from two
 * places — the form, for "this and following", and the detail page, for calling the rest off —
 * and the same codes come back from both. A second copy would drift, and the way it drifts is
 * that one surface keeps showing the general phrase for a refusal the other explains, which is
 * exactly the case somebody most needs the explanation for.
 */
export const EVENT_REFUSALS: Readonly<Record<string, string>> = {
  'event.kind_has_responses': 'events.kindHasResponses',
  'event.recurrence_unbounded': 'events.recurrenceUnbounded',
  'event.recurrence_not_repeating': 'events.recurrenceNotRepeating',
  'event.recurrence_too_many': 'events.recurrenceTooMany',
  'event.recurrence_horizon_too_far': 'events.recurrenceHorizonTooFar',
  'event.recurrence_create_only': 'events.recurrenceCreateOnly',
  'event.series_partly_forbidden': 'events.seriesPartlyForbidden',
  'event.series_move_out_of_range': 'events.seriesMoveOutOfRange',
  'event.not_in_series': 'events.notInSeries',
  'event.recurrence_overlapping': 'events.recurrenceOverlapping',
  'event.recurrence_frequency_invalid': 'events.recurrenceFrequencyInvalid',
  'event.series_frequency_series_only': 'events.seriesFrequencySeriesOnly',
  'event.state_transition_invalid': 'events.stateTransitionInvalid',
  // One sentence for "gone" and "no longer yours to see", because the server gives them one answer
  // and telling them apart would tell somebody which events exist.
  'event.not_found': 'events.goneOrUnreadable',
};

/**
 * The event codes the server has that no event surface here can be refused with, each with the
 * reason. Kept beside the table so that the check holding the table to the server's list of codes
 * has every code accounted for: a new server code is either worded above or argued for here.
 */
export const EVENT_CODES_WITHOUT_WORDS: Readonly<Record<string, string>> = {
  // Both narrow the event list by a value from the list's own choices, which are the server's
  // states and kinds. Only a hand-edited address reaches them, and what it meets is a list that
  // could not be read, not a refusal of anything done to an event.
  'event.kind_invalid': 'list narrowing only',
  'event.state_invalid': 'list narrowing only',
};

/**
 * The translation key for a failure, or the caller's own general phrase when the server said
 * nothing this surface has words for. The fallback is the caller's because "save failed" is the
 * wrong verb for a delete, and a reader told the wrong verb looks for the wrong thing to undo.
 */
export const eventRefusalKey = (error: unknown, fallback: string): string =>
  (error instanceof ApiError ? EVENT_REFUSALS[error.code ?? ''] : undefined) ?? fallback;
