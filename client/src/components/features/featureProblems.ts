// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/**
 * The refusals a write to the feature register can meet — a feature's attributes or parents, a
 * cave's form, a feature set — in the words of somebody who has to act on them.
 *
 * Caves are features, and feature sets name features, so their saves share one table: the cave
 * form, the feature dialog, the parents card and the feature-set page each meet a different few
 * of these codes, but none of them needs its own copy of what a code means.
 *
 * Anything not listed falls back to the general failure sentence: a paraphrase of a code nobody
 * has written wording for would be a guess presented as an explanation.
 *
 * Exported so the wording can be checked against both locales. The keys are looked up through a
 * variable rather than written out at the call, so the scan that catches an untranslated key at a
 * `t('…')` call cannot see them; the test beside this file checks them instead.
 */
export const FEATURE_PROBLEM_MESSAGE_KEYS: Record<string, string> = {
  // Changing a feature's type is decided again for the type it becomes: a rule may let somebody
  // edit one type and not another.
  'feature.type_change_forbidden': 'features.problems.typeChangeForbidden',
  // Replacing the parents would drop a primary parent the caller cannot read, which they would
  // then be unable to put back.
  'feature.parent_primary_hidden': 'features.problems.parentPrimaryHidden',
  // An entrance or a centreline is held under its own cave.
  'feature.parent_fixed': 'features.parentFixed',
  // The cave form offers types from a list read when it opened; one may have been removed since.
  'cave.type_unknown': 'caves.problems.typeUnknown',
  'cave.rock_type_unknown': 'caves.problems.rockTypeUnknown',
  'feature_set.name_invalid': 'featureSets.nameInvalid',
  'feature_set.name_taken': 'featureSets.nameTaken',
  'feature_set.in_use': 'featureSets.inUse',
};

/** The sentence for a failed feature-register write: the refusal's own words, or the general one. */
export function featureProblemMessage(error: unknown, t: TFunction): string {
  const key = error instanceof ApiError && error.code ? FEATURE_PROBLEM_MESSAGE_KEYS[error.code] : undefined;
  return key ? t(key) : t('common.saveFailed');
}
