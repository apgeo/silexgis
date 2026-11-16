// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../../i18n';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { ApiError } from '../../api/client.ts';
import { FEATURE_PROBLEM_MESSAGE_KEYS, featureProblemMessage } from './featureProblems.ts';

/**
 * The codes the feature, cave, parents and feature-set writes refuse with, as the server declares
 * them, with the status each arrives under.
 *
 * Listed here rather than derived, because nothing publishes them: this is the one place the
 * client's side of that contract is written down, and both directions are checked below — a code
 * with no wording, and wording for a code nothing raises.
 */
const codes: [string, number][] = [
  ['feature.type_change_forbidden', 403],
  ['feature.parent_primary_hidden', 409],
  ['feature.parent_fixed', 400],
  ['cave.type_unknown', 400],
  ['cave.rock_type_unknown', 400],
  ['feature_set.name_invalid', 400],
  ['feature_set.name_taken', 400],
  ['feature_set.in_use', 409],
];

/** A dotted key read straight out of a locale file, so a missing entry is not hidden by fallback. */
function lookup(document: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>(
    (node, part) => (node && typeof node === 'object' ? (node as Record<string, unknown>)[part] : undefined),
    document,
  );
}

describe('featureProblemMessage', () => {
  it('names exactly the codes the server raises', () => {
    expect(Object.keys(FEATURE_PROBLEM_MESSAGE_KEYS).sort()).toEqual(codes.map(([code]) => code).sort());
  });

  it('has a non-empty entry for every key in both locales', () => {
    for (const key of Object.values(FEATURE_PROBLEM_MESSAGE_KEYS)) {
      for (const [language, document] of [['en', en], ['ro', ro]] as const) {
        const words = lookup(document, key);
        expect(typeof words, `${language} ${key}`).toBe('string');
        expect((words as string).trim(), `${language} ${key}`).not.toBe('');
      }
    }
  });

  it('has words of its own for every refusal it names, in both languages', () => {
    for (const language of ['en', 'ro']) {
      const t = i18n.getFixedT(language);
      const generic = t('common.saveFailed');
      const seen = new Set<string>();
      for (const [code, status] of codes) {
        const words = featureProblemMessage(new ApiError(status, code), t);
        expect(words, `${language} ${code}`).not.toBe(generic);
        expect(words, `${language} ${code}`).not.toMatch(/^(features|caves|featureSets)\./);
        seen.add(words);
      }
      expect(seen.size, language).toBe(codes.length);
    }
  });

  it('answers anything else as the general failure', () => {
    const t = i18n.getFixedT('en');
    expect(featureProblemMessage(new ApiError(400, 'something.else'), t)).toBe(t('common.saveFailed'));
    expect(featureProblemMessage(new ApiError(500), t)).toBe(t('common.saveFailed'));
    expect(featureProblemMessage(new Error('offline'), t)).toBe(t('common.saveFailed'));
  });
});
