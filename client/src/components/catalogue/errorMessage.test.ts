// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import { speologieErrorMessage, speologieFailureReason, speologieImportErrorMessage } from './errorMessage.ts';

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

  it('sends a refused create permission to the account, not to the catalogue', () => {
    expect(speologieErrorMessage(new ApiError(403, 'access.create_forbidden'), t)).toBe('speologie.errors.createForbidden');
  });
});

describe('speologieImportErrorMessage', () => {
  it('words the import refusals and reads anything else as a failed import, never as an outage', () => {
    expect(speologieImportErrorMessage(new ApiError(403, 'access.create_forbidden'), t)).toBe('speologie.errors.createForbidden');
    expect(speologieImportErrorMessage(new ApiError(400, 'speologie.selection_too_large'), t)).toBe('speologie.errors.selectionTooLarge');
    expect(speologieImportErrorMessage(new ApiError(503, 'speologie.unavailable'), t)).toBe('speologie.errors.unavailable');
    expect(speologieImportErrorMessage(new ApiError(500), t)).toBe('speologie.errors.importFailed');
    expect(speologieImportErrorMessage(new TypeError('Failed to fetch'), t)).toBe('speologie.errors.importFailed');
  });
});

describe('speologieFailureReason', () => {
  it('words a known row code and keeps the server sentence for one it does not know', () => {
    expect(speologieFailureReason({ code: 'speologie.parent_not_found', reason: 'x' }, t)).toBe('speologie.errors.parentNotFound');
    expect(speologieFailureReason({ code: 'speologie.already_imported', reason: 'x' }, t)).toBe('speologie.errors.alreadyImported');
    expect(speologieFailureReason({ code: 'feature.new_rule', reason: 'The server says why.' }, t)).toBe('The server says why.');
  });
});
