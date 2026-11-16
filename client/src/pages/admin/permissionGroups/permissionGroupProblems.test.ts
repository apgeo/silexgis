// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../../i18n';
import { ApiError } from '../../../api/client.ts';
import { accessRuleProblem, permissionGroupNameProblem } from './permissionGroupProblems.ts';

describe('permissionGroupNameProblem', () => {
  it('says why a name was refused, in both languages', () => {
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      const generic = t('common.saveFailed');
      const taken = permissionGroupNameProblem(new ApiError(400, 'permission_group.name_taken'), t);
      const invalid = permissionGroupNameProblem(new ApiError(400, 'permission_group.name_invalid'), t);
      for (const words of [taken, invalid]) {
        expect(words, language).not.toBe(generic);
        expect(words, language).not.toMatch(/^permissionGroups\./);
      }
      expect(taken).not.toBe(invalid);
    }
    const t = i18n.getFixedT('en');
    expect(permissionGroupNameProblem(new ApiError(500), t)).toBe(t('common.saveFailed'));
  });
});

describe('accessRuleProblem', () => {
  // One refusal per wording: codes that share a sentence are listed once.
  const codes = [
    'access_entry.exceeds_own_rights',
    'permission_group.last_full_admin',
    'access_entry.deny_full_administrators',
    'access.subject_unknown',
    'access_entry.anchor_not_found',
    'access_entry.scope_invalid',
    'access.forbidden',
  ];

  it('says why a rule or member write was refused, each in its own words, in both languages', () => {
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      const generic = t('common.saveFailed');
      const words = codes.map((code) => accessRuleProblem(new ApiError(400, code), t));
      for (const [index, sentence] of words.entries()) {
        expect(sentence, `${language} ${codes[index]}`).not.toBe(generic);
        expect(sentence, `${language} ${codes[index]}`).not.toMatch(/^permissionGroups\./);
      }
      expect(new Set(words).size, language).toBe(codes.length);
    }
  });

  it('words a deny on full administration the same whichever write met it', () => {
    const t = i18n.getFixedT('en');
    expect(accessRuleProblem(new ApiError(400, 'permission_group.deny_full_administrators'), t))
      .toBe(accessRuleProblem(new ApiError(400, 'access_entry.deny_full_administrators'), t));
  });

  it('falls back to the general failure for anything it has no words for', () => {
    const t = i18n.getFixedT('en');
    expect(accessRuleProblem(new ApiError(500), t)).toBe(t('common.saveFailed'));
    expect(accessRuleProblem(new ApiError(409, 'something.else'), t)).toBe(t('common.saveFailed'));
    expect(accessRuleProblem(new TypeError('Failed to fetch'), t)).toBe(t('common.saveFailed'));
  });
});
