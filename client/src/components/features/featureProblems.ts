// SPDX-License-Identifier: AGPL-3.0-or-later
import { ApiError, isConcurrencyConflict } from '../../api/client.ts';

/**
 * The refusals a write to the feature register can meet — a shape drawn on the map, a feature's
 * attributes or parents, a cave's form, a feature set — in the words of somebody who has to act on
 * them. A refusal names a rule, and "the save failed" names none of them, so a batch of drawings
 * with one bad shape in it would leave the editor guessing which one and why.
 *
 * Caves are features, and feature sets name features, so their saves share one table: the map
 * editor, the cave form, the feature dialog, the parents card and the feature-set page each meet a
 * different few of these codes, but none of them needs its own copy of what a code means.
 *
 * Exported so the wording can be checked against both locales. The keys are looked up through a
 * variable rather than written out at the call, so the scan that catches an untranslated key at a
 * `t('…')` call cannot see them; the test beside this file checks them instead.
 */
export const FEATURE_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  'feature.geometry_invalid': 'mapEdit.problems.geometryInvalid',
  'feature.type_required': 'mapEdit.problems.typeRequired',
  'feature.type_bound': 'mapEdit.problems.typeNotAllowed',
  // Changing a feature's type is decided again for the type it becomes: a rule may let somebody
  // edit one type and not another.
  'feature.type_change_forbidden': 'features.problems.typeChangeForbidden',
  'feature.properties_invalid': 'mapEdit.problems.propertiesInvalid',
  'feature.parent_required': 'mapEdit.problems.parentRequired',
  'feature.parent_not_found': 'mapEdit.problems.parentUnavailable',
  // Replacing the parents would drop a primary parent the caller cannot read, which they would
  // then be unable to put back.
  'feature.parent_primary_hidden': 'features.problems.parentPrimaryHidden',
  // An entrance or a centreline is held under its own cave.
  'feature.parent_fixed': 'features.parentFixed',
  'feature.not_found': 'mapEdit.problems.notFound',
  // The cave form offers types from a list read when it opened; one may have been removed since.
  'cave.type_unknown': 'caves.problems.typeUnknown',
  'cave.rock_type_unknown': 'caves.problems.rockTypeUnknown',
  'feature_set.name_invalid': 'featureSets.nameInvalid',
  'feature_set.name_taken': 'featureSets.nameTaken',
  'feature_set.in_use': 'featureSets.inUse',
  // A feature's links to other features, replaced as a whole by the links card.
  'feature.link_self': 'features.problems.linkSelf',
  'feature.link_kind_unknown': 'features.problems.linkKindUnknown',
  'feature.link_duplicate': 'features.problems.linkDuplicate',
  'feature.link_target_not_found': 'features.problems.linkTargetNotFound',
};

/**
 * The sentence for a refused feature-register write: the refusal's own words, or the plain failure
 * of the act (`fallbackKey`) for an answer nobody has written wording for — a paraphrase of an
 * unknown code would be a guess presented as an explanation.
 *
 * A concurrency refusal is worded as somebody else's change. A write that goes through the shared
 * mutation client is already answered by its conflict warning, so such a caller leaves conflicts
 * out rather than showing this sentence beside it; a write made outside it (the map editor's batch
 * save) shows this one.
 */
export function featureProblemMessage(
  error: unknown,
  t: (key: string) => string,
  fallbackKey: 'common.saveFailed' | 'common.deleteFailed' = 'common.saveFailed',
): string {
  if (!(error instanceof ApiError)) {
    return t(fallbackKey);
  }
  const key = error.code ? FEATURE_PROBLEM_MESSAGE_KEYS[error.code] : undefined;
  if (key) {
    return t(key);
  }
  // What counts as somebody else's change is decided in one place for the whole client.
  if (isConcurrencyConflict(error)) {
    return t('common.conflict');
  }
  if (error.status === 403) {
    return t('mapEdit.problems.forbidden');
  }
  // A missing feature (or cave, which is one) is worded as such; a missing feature set is not a
  // feature, and falls back to the plain failure.
  if (error.status === 404 && !error.code?.startsWith('feature_set.')) {
    return t('mapEdit.problems.notFound');
  }
  return t(fallbackKey);
}
