// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../../i18n';
import { ApiError } from '../../../api/client.ts';
import { permissionGroupNameProblem } from './permissionGroupProblems.ts';

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
