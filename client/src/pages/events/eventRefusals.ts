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
};

/**
 * The translation key for a failure, or the caller's own general phrase when the server said
 * nothing this surface has words for. The fallback is the caller's because "save failed" is the
 * wrong verb for a delete, and a reader told the wrong verb looks for the wrong thing to undo.
 */
export const eventRefusalKey = (error: unknown, fallback: string): string =>
  (error instanceof ApiError ? EVENT_REFUSALS[error.code ?? ''] : undefined) ?? fallback;
