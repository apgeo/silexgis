// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  ACCOUNT_PREF_DEFAULTS,
  PREFERENCES_MAX_LENGTH,
  accountPrefsIn,
  accountPrefsPatchSince,
  applyMergePatch,
  changedSince,
  fitToBudget,
  mergeAccountPrefs,
  mergeLayouts,
  pickAccountPrefs,
  planAccountWrite,
  readAccountPrefs,
  sanitizeAccountPrefs,
  withoutLayouts,
  type AccountPrefs,
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

describe('accountPrefsPatchSince', () => {
  const stored = {
    appearance: { theme: 'dark', density: 'comfortable', reduceMotion: false },
    panels: { main: { width: 30 }, scene3d: { width: 25 } },
    somethingElse: { written: 'by another client' },
  };

  it('removes what went back to its default here and leaves what it does not own alone', () => {
    const base = accountPrefsIn(stored);
    const patch = accountPrefsPatchSince(stored, base, { panels: { main: { width: 30 } } });

    expect(patch).toEqual({ appearance: null, panels: { scene3d: null } });
    expect(applyMergePatch(stored, patch)).toEqual({
      panels: { main: { width: 30 } },
      somethingElse: { written: 'by another client' },
    });
  });

  it('replaces a list whole rather than merging it', () => {
    const patch = accountPrefsPatchSince({ pinnedTypeIds: [3, 4] }, { pinnedTypeIds: [3, 4] }, { pinnedTypeIds: [4] });
    expect(patch).toEqual({ pinnedTypeIds: [4] });
  });

  it('is empty when nothing was changed here', () => {
    expect(accountPrefsPatchSince({ coordinateFormat: 'dms' }, { coordinateFormat: 'dms' }, { coordinateFormat: 'dms' })).toEqual({});
  });
});

/**
 * Two tabs — or a desktop and a laptop — signed in to the same account, each holding the account's
 * preferences as it last read them. What one changes must not be undone by the other's next save.
 */
describe('two browsers writing to one account', () => {
  /** One browser: what it last knew the account to hold, and what it holds now. */
  interface Tab {
    base: AccountPrefs;
    local: AccountPrefs;
  }

  /** The server's merging write, applied as it applies it. */
  let server: Record<string, unknown>;

  function open(): Tab {
    const read = accountPrefsIn(server);
    return { base: read, local: read };
  }

  /** A save: this tab's changes only, merged on the server; the answer becomes its baseline. */
  function save(tab: Tab) {
    const write = planAccountWrite(server, tab.base, tab.local)!;
    server = applyMergePatch(server, write.patch) as Record<string, unknown>;
    const answer = accountPrefsIn(server);
    tab.local = mergeAccountPrefs(withoutLayouts(tab.local, write.leftOut), tab.local, answer);
    tab.base = answer;
  }

  const layout = (id: string) => ({ id, name: `Layout ${id}`, panels: { main: { width: 30 } } });

  it('keeps a format chosen on the desktop when the laptop then drags a panel', () => {
    server = {};
    const desktop = open();
    const laptop = open();

    desktop.local = { ...desktop.local, coordinateFormat: 'utm' };
    save(desktop);
    laptop.local = { ...laptop.local, panels: { main: { width: 30 } } };
    save(laptop);

    expect(server).toEqual({ coordinateFormat: 'utm', panels: { main: { width: 30 } } });
    // And the laptop has caught up with the desktop's choice by its own save.
    expect(laptop.local.coordinateFormat).toBe('utm');
  });

  it('keeps a layout saved in one browser when another saves one of its own', () => {
    server = { layouts: [layout('a')] };
    const desktop = open();
    const laptop = open();

    desktop.local = { ...desktop.local, layouts: [layout('a'), layout('b')] };
    save(desktop);
    laptop.local = { ...laptop.local, layouts: [layout('a'), layout('c')] };
    save(laptop);

    expect((server.layouts as { id: string }[]).map((l) => l.id)).toEqual(['a', 'b', 'c']);
  });

  it('removes a layout removed in one browser, and does not bring it back from the other', () => {
    server = { layouts: [layout('a'), layout('b')] };
    const desktop = open();
    const laptop = open();

    desktop.local = { ...desktop.local, layouts: [layout('b')] };
    save(desktop);
    laptop.local = { ...laptop.local, coordinateFormat: 'dms' };
    save(laptop);

    expect((server.layouts as { id: string }[]).map((l) => l.id)).toEqual(['b']);
    expect((laptop.local.layouts as { id: string }[]).map((l) => l.id)).toEqual(['b']);
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

describe('mergeAccountPrefs', () => {
  it('takes the account’s value of whatever was not changed here, defaults included', () => {
    const base = { coordinateFormat: 'dms', pinnedTypeIds: [1] };
    expect(mergeAccountPrefs(base, base, { coordinateFormat: 'utm' })).toEqual({ coordinateFormat: 'utm' });
  });

  it('keeps a change made here that has not reached the account — a reload moments after it', () => {
    // Put back to the default here (the compared form leaves defaults out): the default stays.
    expect(mergeAccountPrefs({ coordinateFormat: 'dms' }, {}, { coordinateFormat: 'dms' })).toEqual({});
    expect(mergeAccountPrefs({}, { overviewMap: true }, { coordinateFormat: 'utm' })).toEqual({
      overviewMap: true,
      coordinateFormat: 'utm',
    });
  });
});

describe('mergeLayouts', () => {
  const a = { id: 'a', name: 'A' };
  const b = { id: 'b', name: 'B' };
  const c = { id: 'c', name: 'C' };

  it('takes what was added and removed elsewhere, keeps what was added and changed here', () => {
    const renamed = { id: 'a', name: 'A, renamed here' };
    expect(mergeLayouts([a, b], [renamed, b, c], [a])).toEqual([renamed, c]);
  });

  it('keeps a layout this browser has that the account never had, through any number of merges', () => {
    // Left out of the account's document to fit its limit: not in the baseline, so not removed.
    expect(mergeLayouts([a], [a, b], [a])).toEqual([a, b]);
    expect(changedSince({ layouts: [a] }, { layouts: [a, b] })).toBe(true);
  });
});
