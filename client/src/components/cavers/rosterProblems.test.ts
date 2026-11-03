// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { rosterWriteProblem } from './rosterProblems.ts';

describe('rosterWriteProblem', () => {
  const codes = [
    'caver.grants_full_admin',
    'caver.revokes_full_admin',
    'access_entry.exceeds_own_rights',
    'caving_group.member_raced',
    'permission_group.last_full_admin',
  ];

  it('has words of its own for every refusal it names, in both languages', () => {
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      const generic = t('common.saveFailed');
      const seen = new Set<string>();
      for (const code of codes) {
        const words = rosterWriteProblem(new ApiError(403, code), t);
        expect(words, `${language} ${code}`).not.toBe(generic);
        expect(words, `${language} ${code}`).not.toMatch(/^cavers\./);
        seen.add(words);
      }
      expect(seen.size, language).toBe(codes.length);
    }
  });

  it('answers anything else as the general failure', () => {
    const t = i18n.getFixedT('en');
    expect(rosterWriteProblem(new ApiError(400, 'something.else'), t)).toBe(t('common.saveFailed'));
    expect(rosterWriteProblem(new Error('offline'), t)).toBe(t('common.saveFailed'));
  });
});
