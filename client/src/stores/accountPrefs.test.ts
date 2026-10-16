// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  ACCOUNT_PREF_DEFAULTS,
  PREFERENCES_MAX_LENGTH,
  accountPrefsPatch,
  applyMergePatch,
  fitToBudget,
  pickAccountPrefs,
  prefsToApply,
  readAccountPrefs,
  sanitizeAccountPrefs,
} from './accountPrefs.ts';

describe('applyMergePatch', () => {
  // The examples of RFC 7396, appendix A — the rule the server's merging write follows.
  it.each([
    [{ a: 'b' }, { a: 'c' }, { a: 'c' }],
    [{ a: 'b' }, { b: 'c' }, { a: 'b', b: 'c' }],
    [{ a: 'b' }, { a: null }, {}],
    [{ a: 'b', b: 'c' }, { a: null }, { b: 'c' }],
    [{ a: ['b'] }, { a: 'c' }, { a: 'c' }],
    [{ a: 'c' }, { a: ['b'] }, { a: ['b'] }],
    [{ a: { b: 'c' } }, { a: { b: 'd', c: null } }, { a: { b: 'd' } }],
    [{ a: [{ b: 'c' }] }, { a: [1] }, { a: [1] }],
    [['a', 'b'], ['c', 'd'], ['c', 'd']],
    [{ a: 'b' }, ['c'], ['c']],
    [{ a: 'foo' }, null, null],
    [{ a: 'foo' }, 'bar', 'bar'],
    [{ e: null }, { a: 1 }, { e: null, a: 1 }],
    [[1, 2], { a: 'b', c: null }, { a: 'b' }],
    [{}, { a: { bb: { ccc: null } } }, { a: { bb: {} } }],
  ])('%j patched with %j is %j', (target, patch, expected) => {
    expect(applyMergePatch(target, patch)).toEqual(expected);
  });
});

describe('pickAccountPrefs', () => {
  it('keeps only what differs from the defaults', () => {
    expect(pickAccountPrefs({ ...ACCOUNT_PREF_DEFAULTS })).toEqual({});
    expect(
      pickAccountPrefs({
        ...ACCOUNT_PREF_DEFAULTS,
        coordinateFormat: 'utm',
        panels: { main: { width: 30, order: undefined }, popout: {} },
      }),
    ).toEqual({ coordinateFormat: 'utm', panels: { main: { width: 30 } } });
  });

  it('says nothing about a panel whose every choice is still the default', () => {
    expect(pickAccountPrefs({ panels: { main: { order: undefined, hidden: undefined } } })).toEqual({});
  });
});

describe('accountPrefsPatch', () => {
  const stored = {
    appearance: { theme: 'dark', density: 'comfortable', reduceMotion: false },
    panels: { main: { width: 30 }, scene3d: { width: 25 } },
    somethingElse: { written: 'by another client' },
  };

  it('removes what went back to its default and leaves what it does not own alone', () => {
    const patch = accountPrefsPatch(stored, { panels: { main: { width: 30 } } });

    expect(patch).toEqual({ appearance: null, panels: { scene3d: null } });
    expect(applyMergePatch(stored, patch)).toEqual({
      panels: { main: { width: 30 } },
      somethingElse: { written: 'by another client' },
    });
  });

  it('replaces a list whole rather than merging it', () => {
    const patch = accountPrefsPatch({ pinnedTypeIds: [3, 4] }, { pinnedTypeIds: [4] });
    expect(patch).toEqual({ pinnedTypeIds: [4] });
  });

  it('is empty when the stored document already says it', () => {
    expect(accountPrefsPatch({ coordinateFormat: 'dms' }, { coordinateFormat: 'dms' })).toEqual({});
  });
});

describe('fitToBudget', () => {
  const layout = (n: number) => ({ id: `l${n}`, name: `Layout ${n}`, panels: { main: { width: 20 + n } } });

  it('leaves a document within the limit untouched', () => {
    const document = { layouts: [layout(1)], coordinateFormat: 'utm' };
    expect(fitToBudget(document)).toEqual({ document, leftOutLayouts: 0 });
  });

  it('leaves out the oldest layouts until the document fits', () => {
    const layouts = Array.from({ length: 20 }, (_, n) => layout(n));
    const size = JSON.stringify({ layouts }).length;
    const fitted = fitToBudget({ layouts }, size - 10)!;

    expect(fitted.leftOutLayouts).toBe(1);
    expect((fitted.document.layouts as { id: string }[])[0].id).toBe('l1');
    expect(JSON.stringify(fitted.document).length).toBeLessThanOrEqual(size - 10);
  });

  it('gives up when there are no layouts left to leave out', () => {
    expect(fitToBudget({ selectors: { x: 'y'.repeat(PREFERENCES_MAX_LENGTH) } })).toBeNull();
  });

  it('holds a realistic full workspace well inside the server limit', () => {
    // Every account preference set, at the sizes a heavy user reaches: three panels arranged, six
    // named layouts, a dozen selectors, a dozen pinned types.
    const panel = {
      order: ['details', 'tags', 'attachments', 'links', 'text', 'history'],
      collapsed: ['history'],
      hidden: ['text'],
      width: 32,
      density: 'compact',
      pinned: false,
    };
    const full = pickAccountPrefs({
      appearance: { theme: 'dark', density: 'compact', reduceMotion: true },
      panels: { main: panel, scene3d: panel, popout: panel },
      layouts: Array.from({ length: 6 }, (_, n) => ({
        id: `00000000-0000-4000-8000-00000000000${n}`,
        name: `Arrangement ${n}`,
        panels: { main: panel, scene3d: panel, popout: panel },
        mapChromeHidden: false,
      })),
      selectors: Object.fromEntries(
        Array.from({ length: 12 }, (_, n) => [`selector-${n}`, { presentation: 'dropdown', scope: 'caves' }]),
      ),
      docks: { leftHidden: true, rightHidden: false },
      pinnedTypeIds: Array.from({ length: 12 }, (_, n) => n + 1),
      coordinateFormat: 'utm',
      overviewMap: true,
    });

    expect(JSON.stringify(full).length).toBeLessThan(PREFERENCES_MAX_LENGTH);
  });
});

describe('readAccountPrefs', () => {
  it('reads only the account preferences out of the stored document', () => {
    expect(readAccountPrefs({ coordinateFormat: 'dms', unrelated: 1 })).toEqual({ coordinateFormat: 'dms' });
    expect(readAccountPrefs('not a document')).toEqual({});
  });
});

describe('sanitizeAccountPrefs', () => {
  it('drops a value this client cannot draw, and keeps the rest', () => {
    expect(
      sanitizeAccountPrefs({
        appearance: { theme: 'sepia', density: 'compact', reduceMotion: false },
        coordinateFormat: 'stereo70',
        pinnedTypeIds: [1, 2],
        overviewMap: 'yes',
        docks: { leftHidden: true },
      }),
    ).toEqual({ pinnedTypeIds: [1, 2], docks: { leftHidden: true, rightHidden: false } });
  });
});

describe('prefsToApply', () => {
  it('takes a preference the account does not store as its default', () => {
    const applied = prefsToApply({ coordinateFormat: 'utm' }, 'same');
    expect(applied.coordinateFormat).toBe('utm');
    expect(applied.pinnedTypeIds).toEqual([]);
    expect(applied.overviewMap).toBe(false);
  });

  it('keeps a change made here that had not reached the account when the page was reloaded', () => {
    expect(prefsToApply({ coordinateFormat: 'utm' }, 'same', true)).toEqual({});
  });

  it('on the first claim of an unclaimed cache, leaves alone what the account has not stored', () => {
    expect(prefsToApply({ coordinateFormat: 'utm' }, 'claimed')).toEqual({ coordinateFormat: 'utm' });
  });
});
