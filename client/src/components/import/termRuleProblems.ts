// SPDX-License-Identifier: AGPL-3.0-or-later
import type { TFunction } from 'i18next';
import { ApiError } from '../../api/client.ts';

/** Refusals of a rule set that have a sentence of their own, whichever act was refused. */
export const TERM_RULE_REFUSALS: Readonly<Record<string, string>> = {
  'term_rules.not_editable': 'termRules.errors.notEditable',
  'term_rules.not_found': 'termRules.errors.notFound',
  'term_rules.group_not_found': 'termRules.errors.groupNotFound',
  'term_rules.seeded_undeletable': 'termRules.errors.seededUndeletable',
  'term_rules.promotion_forbidden': 'termRules.errors.promotionForbidden',
};

/**
 * Why an act on a rule set was refused. A refusal with a code of its own is said as that; anything
 * else in the words of the act that failed, given as `fallbackKey`.
 */
export function termRuleProblem(error: unknown, t: TFunction, fallbackKey: string): string {
  const known = error instanceof ApiError && error.code ? TERM_RULE_REFUSALS[error.code] : undefined;
  return t(known ?? fallbackKey);
}
