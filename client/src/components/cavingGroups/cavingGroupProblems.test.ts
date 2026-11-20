// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { announcementProblem } from './cavingGroupProblems.ts';

describe('announcementProblem', () => {
  // Both refusals fail the same way however often the send is pressed again, so "try again" is
  // the one thing they must not say.
  it('names the wait and the spending ceiling, in both languages, instead of "try again"', () => {
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      const generic = t('cavingGroups.announce.failed');
      const tooSoon = announcementProblem(new ApiError(400, 'caving_group.announcement_too_soon'), t);
      const capReached = announcementProblem(new ApiError(400, 'caving_group.announcement_paid_cap_reached'), t);
      for (const words of [tooSoon, capReached]) {
        expect(words, language).not.toBe(generic);
        expect(words, language).not.toMatch(/^cavingGroups\./);
      }
      expect(tooSoon, language).not.toBe(capReached);
    }
  });

  it('answers anything else as the general failure', () => {
    const t = i18n.getFixedT('en');
    expect(announcementProblem(new ApiError(500, undefined), t)).toBe(t('cavingGroups.announce.failed'));
    expect(announcementProblem(new Error('offline'), t)).toBe(t('cavingGroups.announce.failed'));
  });
});
