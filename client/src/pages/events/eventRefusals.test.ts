// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { EVENT_REFUSALS, eventRefusalKey } from './eventRefusals.ts';

function lookup(catalogue: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], catalogue);
}

describe('the event refusal wording', () => {
  it('has every sentence in both languages', () => {
    for (const key of Object.values(EVENT_REFUSALS)) {
      expect(typeof lookup(en, key), `${key} in English`).toBe('string');
      expect(typeof lookup(ro, key), `${key} in Romanian`).toBe('string');
    }
  });

  it('words a repeating event that overlaps its own next occurrence', () => {
    expect(eventRefusalKey(new ApiError(400, 'event.recurrence_overlapping', 'server prose'), 'common.saveFailed')).toBe(
      'events.recurrenceOverlapping',
    );
  });

  it("falls back to the caller's phrase, never to the server's prose", () => {
    expect(eventRefusalKey(new ApiError(400, 'event.something_new', 'server prose'), 'common.deleteFailed')).toBe(
      'common.deleteFailed',
    );
    expect(eventRefusalKey(new TypeError('Failed to fetch'), 'common.saveFailed')).toBe('common.saveFailed');
  });
});
