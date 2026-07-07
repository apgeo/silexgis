// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import ro from './locales/ro.json';

function flattenKeys(value: object, prefix = ''): string[] {
  return Object.entries(value).flatMap(([key, child]) =>
    typeof child === 'object' && child !== null
      ? flattenKeys(child, `${prefix}${key}.`)
      : [`${prefix}${key}`],
  );
}

// EN and RO must be maintained together (ADR-016).
describe('i18n locales', () => {
  it('en and ro define exactly the same keys', () => {
    expect(flattenKeys(ro).sort()).toEqual(flattenKeys(en).sort());
  });

  it('no empty translations', () => {
    const empty = (locale: object) =>
      flattenKeys(locale).filter((key) =>
        key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown>)[part], locale) === '',
      );
    expect(empty(en)).toEqual([]);
    expect(empty(ro)).toEqual([]);
  });
});
