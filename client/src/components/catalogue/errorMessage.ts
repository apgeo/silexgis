// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError } from '../../api/client.ts';

/**
 * The catalogue fails in several distinguishable ways and they lead to different people: an
 * unset key and a rejected key are an administrator's, an unreachable service is nobody's and
 * will pass, and a rejected request is this application's own defect.
 */
/** The catalogue's own refusals: what the service, its key or the question asked of it did. */
const CATALOGUE_KEYS: Readonly<Record<string, string>> = {
  'speologie.not_configured': 'speologie.errors.notConfigured',
  'speologie.unauthorized': 'speologie.errors.unauthorized',
  'speologie.unavailable': 'speologie.errors.unavailable',
  'speologie.rejected': 'speologie.errors.rejected',
  'speologie.search_too_broad': 'speologie.errors.searchTooBroad',
  'speologie.search_too_deep': 'speologie.errors.searchTooDeep',
  'speologie.cave_not_found': 'speologie.errors.caveNotFound',
  // Both search and import are open only to an account that may create caves.
  'access.create_forbidden': 'speologie.errors.createForbidden',
};

/**
 * What an import refuses, for the whole selection or for one cave of it. A cave refused on its
 * own is listed with the same code, so both are worded from this one table.
 */
const IMPORT_KEYS: Readonly<Record<string, string>> = {
  'speologie.selection_empty': 'speologie.errors.selectionEmpty',
  'speologie.selection_too_large': 'speologie.errors.selectionTooLarge',
  'speologie.nothing_created': 'speologie.errors.nothingCreated',
  'speologie.record_missing': 'speologie.errors.recordMissing',
  'speologie.update_forbidden': 'speologie.errors.updateForbidden',
  'speologie.cave_missing': 'speologie.errors.caveMissing',
  'speologie.position_invalid': 'speologie.errors.positionInvalid',
  'speologie.type_unknown': 'speologie.errors.typeUnknown',
  'speologie.parent_not_found': 'speologie.errors.parentNotFound',
};

const codeOf = (error: unknown) => (error instanceof ApiError ? error.code : undefined);

export function speologieErrorMessage(error: unknown, t: (key: string) => string): string | null {
  if (!error) {
    return null;
  }

  const key = CATALOGUE_KEYS[codeOf(error) ?? ''];
  // A search or a lookup that failed for a reason this screen cannot name is most often the
  // volunteer-run service being away; nothing else on these routes fails without a code.
  return t(key ?? 'speologie.errors.unavailable');
}

/**
 * The wording of a refused import. Unlike a search, an import fails for reasons of its own — a
 * permission, the selection, a chosen parent — and one it cannot name is reported as the import
 * failing, not as the catalogue being away, which would send the reader to the wrong person.
 */
export function speologieImportErrorMessage(error: unknown, t: (key: string) => string): string {
  const code = codeOf(error) ?? '';
  return t(CATALOGUE_KEYS[code] ?? IMPORT_KEYS[code] ?? 'speologie.errors.importFailed');
}

/**
 * Why one cave of an import was left out. A code this screen knows is worded in the reader's
 * language; any other keeps the server's own sentence, which is better than no reason at all.
 */
export function speologieFailureReason(
  failure: { code: string; reason: string },
  t: (key: string) => string,
): string {
  const key = IMPORT_KEYS[failure.code] ?? CATALOGUE_KEYS[failure.code];
  return key ? t(key) : failure.reason;
}
