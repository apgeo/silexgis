// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import { ApiError } from '../../api/client.ts';
import { settingsNotice, settingsSaveProblem } from './settingsProblems.ts';

describe('settingsSaveProblem', () => {
  it('says an internal gateway address was refused, in both languages, apart from a failed save', () => {
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      const internal = settingsSaveProblem(new ApiError(400, 'settings.sms_gateway_internal'), t);
      expect(internal, language).not.toBe(t('common.saveFailed'));
      expect(internal, language).not.toBe(t('admin.settingsProblems.invalid'));
      expect(internal, language).not.toMatch(/^admin\./);
      // The settings forms meet the refusal as the body the server wrote, not as a thrown error.
      expect(settingsSaveProblem({ code: 'settings.sms_gateway_internal' }, t), language).toBe(internal);
    }
  });
});

describe('settingsNotice', () => {
  const codes = ['settings.mail_password_cleared', 'settings.sms_auth_header_cleared'];

  it('words each note a save came back with, each in its own words, in both languages', () => {
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      const words = codes.map((code) => settingsNotice(code, t));
      for (const [index, sentence] of words.entries()) {
        expect(sentence, `${language} ${codes[index]}`).not.toMatch(/^admin\./);
      }
      expect(new Set(words).size, language).toBe(codes.length);
    }
    const en = i18n.getFixedT('en');
    const ro = i18n.getFixedT('ro');
    expect(settingsNotice(codes[0], ro)).not.toBe(settingsNotice(codes[0], en));
  });

  it('still says something for a note it has no words for, rather than dropping it', () => {
    const t = i18n.getFixedT('en');
    const unknown = settingsNotice('settings.something_new', t);
    expect(unknown).not.toMatch(/^admin\./);
    expect(codes.map((code) => settingsNotice(code, t))).not.toContain(unknown);
  });
});
