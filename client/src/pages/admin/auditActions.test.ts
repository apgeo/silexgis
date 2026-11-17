// SPDX-License-Identifier: AGPL-3.0-or-later
import { readFileSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import en from '../../i18n/locales/en.json';
import ro from '../../i18n/locales/ro.json';
import { AUDIT_ACTION_KEYS } from './auditActions.ts';

/** A dotted key read straight out of a locale file, so a missing entry is not hidden by fallback. */
function lookup(document: unknown, key: string): unknown {
  return key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], document);
}

describe('the audit trail action words', () => {
  // The actions are declared once, on the server; one added there and not here would reach a
  // Romanian reader as an English identifier.
  it('has words for every action the server declares, in both languages', () => {
    const declared = readFileSync(
      path.resolve(process.cwd(), '../server/src/SilexGis.Domain/Entities/AuditEntry.cs'),
      'utf8',
    );
    const block = declared.slice(declared.indexOf('class AuditActions'));
    const actions = [...block.matchAll(/public const string \w+ = "([a-z_]+)";/g)].map((match) => match[1]);
    expect(actions.length).toBeGreaterThan(5);
    expect(actions.filter((action) => !(action in AUDIT_ACTION_KEYS))).toEqual([]);
    for (const key of Object.values(AUDIT_ACTION_KEYS)) {
      expect(typeof lookup(en, key), `en ${key}`).toBe('string');
      expect(typeof lookup(ro, key), `ro ${key}`).toBe('string');
    }
  });
});
