// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import { speologieErrorMessage } from './errorMessage.ts';

const t = (key: string) => key;

describe('speologieErrorMessage', () => {
  it('sends a search too deep to page through to narrowing it, not to an outage', () => {
    const refused = new ApiError(400, 'speologie.search_too_deep', 'An English sentence the reader is never shown.');

    expect(speologieErrorMessage(refused, t)).toBe('speologie.errors.searchTooDeep');
  });

  it('still reads an unknown failure as the catalogue being unavailable', () => {
    expect(speologieErrorMessage(new Error('boom'), t)).toBe('speologie.errors.unavailable');
    expect(speologieErrorMessage(null, t)).toBeNull();
  });
});
