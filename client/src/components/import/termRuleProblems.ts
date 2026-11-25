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

/**
 * Why a whole set sent to the server — saved from the editor or imported from a file — was
 * refused. The server checks the whole document — a pattern that cannot be compiled, a rule naming
 * a taxonomy its target does not use — and for that its own sentence is the useful one, since it
 * names the rule that is wrong; no wording here could. Other refusals are worded by their code,
 * and a failure that says nothing is said in the words of the act, given as `fallbackKey`.
 */
export function termRuleSaveProblem(error: unknown, t: TFunction, fallbackKey: string): string {
  if (!(error instanceof ApiError)) {
    return t(fallbackKey);
  }
  const known = error.code ? TERM_RULE_REFUSALS[error.code] : undefined;
  if (known) {
    return t(known);
  }
  if (error.status === 403) {
    return t('termRules.errors.forbidden');
  }
  if (error.status >= 400 && error.status < 500 && error.detail) {
    return error.detail;
  }
  return t(fallbackKey);
}
