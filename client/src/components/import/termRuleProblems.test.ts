// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { termRuleProblem } from './termRuleProblems.ts';

describe('termRuleProblem', () => {
  it('says the shipped set cannot be deleted and who may promote one, in both languages', () => {
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      const seeded = termRuleProblem(new ApiError(400, 'term_rules.seeded_undeletable'), t, 'common.deleteFailed');
      const promotion = termRuleProblem(new ApiError(403, 'term_rules.promotion_forbidden'), t, 'termRules.errors.promoteFailed');
      expect(seeded, language).not.toBe(t('common.deleteFailed'));
      expect(promotion, language).not.toBe(t('termRules.errors.promoteFailed'));
      expect(seeded, language).not.toMatch(/^termRules\./);
      expect(promotion, language).not.toMatch(/^termRules\./);
    }
  });

  it('says a failure with no code in the words of the act that failed', () => {
    const t = i18n.getFixedT('en');
    expect(termRuleProblem(new Error('offline'), t, 'common.deleteFailed')).toBe(t('common.deleteFailed'));
    expect(termRuleProblem(new ApiError(503, undefined), t, 'termRules.errors.promoteFailed'))
      .toBe(t('termRules.errors.promoteFailed'));
  });
});
