// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { ApiError } from '../../api/client.ts';
import { CENTERLINE_PROBLEM_MESSAGE_KEYS, centerlineProblemMessage } from './centerlineProblems.ts';

/**
 * The codes the centerline upload, edit and delete refuse with. Listed here rather than derived,
 * because nothing publishes them; both directions are checked — a code with no wording, and
 * wording for a code nothing raises.
 */
const codes = [
  'centerline.format_unsupported',
  'centerline.size_invalid',
  'centerline.unreadable',
  'centerline.no_lines',
  'centerline.survey_model_invalid',
  'centerline.default_required',
  'centerline.not_found',
  'cave.not_found',
  'acl.forbidden',
];

function lookup(document: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>(
    (node, part) => (node && typeof node === 'object' ? (node as Record<string, unknown>)[part] : undefined),
    document,
  );
}

describe('centerline refusal wording', () => {
  it('words exactly the codes the server raises', () => {
    expect(Object.keys(CENTERLINE_PROBLEM_MESSAGE_KEYS).sort()).toEqual([...codes].sort());
  });

  it.each(Object.values(CENTERLINE_PROBLEM_MESSAGE_KEYS))('has %s in both locales', (key) => {
    expect(typeof lookup(en, key)).toBe('string');
    expect(typeof lookup(ro, key)).toBe('string');
  });

  it('says what to do about a file the server could not read, and falls back for anything else', async () => {
    await i18n.changeLanguage('en');
    expect(centerlineProblemMessage(new ApiError(400, 'centerline.no_lines'), i18n.t, 'centerlines.uploadFailed')).toMatch(
      /no lines/,
    );
    expect(centerlineProblemMessage(new ApiError(500, undefined), i18n.t, 'centerlines.uploadFailed')).toBe(
      i18n.t('centerlines.uploadFailed'),
    );
    expect(centerlineProblemMessage(new Error('x'), i18n.t, 'common.saveFailed')).toBe(i18n.t('common.saveFailed'));
  });
});
