// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError, isConcurrencyConflict } from '../../api/client.ts';

/**
 * Why the server refused to create or change a feature drawn on the map, as a sentence the
 * editor can act on. A refusal names a rule — the shape, the type, the attributes, a parent, the
 * person's rights, somebody else's newer change — and "the save failed" names none of them, so a
 * batch of drawings with one bad shape in it would leave the editor guessing which one and why.
 * An answer this list does not know falls back to the plain failure sentence.
 */
export function featureWriteProblem(
  error: unknown,
  t: (key: string) => string,
  fallbackKey: 'common.saveFailed' | 'common.deleteFailed' = 'common.saveFailed',
): string {
  if (!(error instanceof ApiError)) {
    return t(fallbackKey);
  }
  switch (error.code) {
    case 'feature.geometry_invalid':
      return t('mapEdit.problems.geometryInvalid');
    case 'feature.type_required':
      return t('mapEdit.problems.typeRequired');
    case 'feature.type_bound':
    case 'feature.type_change_forbidden':
      return t('mapEdit.problems.typeNotAllowed');
    case 'feature.properties_invalid':
      return t('mapEdit.problems.propertiesInvalid');
    case 'feature.parent_required':
      return t('mapEdit.problems.parentRequired');
    case 'feature.parent_not_found':
    case 'feature.parent_primary_hidden':
      return t('mapEdit.problems.parentUnavailable');
    case 'feature.not_found':
      return t('mapEdit.problems.notFound');
    default:
      break;
  }
  // What counts as somebody else's change is decided in one place for the whole client.
  if (isConcurrencyConflict(error)) {
    return t('common.conflict');
  }
  if (error.status === 403) {
    return t('mapEdit.problems.forbidden');
  }
  if (error.status === 404) {
    return t('mapEdit.problems.notFound');
  }
  return t(fallbackKey);
}
