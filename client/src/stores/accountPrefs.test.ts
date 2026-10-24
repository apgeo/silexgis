// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { accountPrefDefaults, MAX_PINNED_FILTERS } from './uiPrefsStore.ts';
import {
  ACCOUNT_PREF_DEFAULTS,
  ACCOUNT_PREF_KEYS,
  PREFERENCES_MAX_LENGTH,
  accountPrefsIn,
  accountPrefsPatchSince,
  applyMergePatch,
  baselineAfterLeaving,
  changedSince,
  layoutsNotIn,
  mergeAccountPrefs,
  mergeLayouts,
  pickAccountPrefs,
  planAccountWrite,
  planLeavingWrite,
  preferencesLength,
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

describe('planAccountWrite held to the limit', () => {
  const layout = (n: number) => ({ id: `l${n}`, name: `Layout ${n}`, panels: { main: { width: 20 + n } } });
  const ids = (layouts: unknown) => (layouts as { id: string }[]).map((l) => l.id);

  it('sends this browser’s changes as they are when the document fits', () => {
    expect(planAccountWrite({}, {}, { layouts: [layout(1)], coordinateFormat: 'utm' })).toEqual({
      patch: { layouts: [layout(1)], coordinateFormat: 'utm' },
      leftOut: [],
    });
  });

  it('leaves out the newest layouts added here until the document fits', () => {
    const layouts = Array.from({ length: 20 }, (_, n) => layout(n));
    const size = JSON.stringify({ layouts }).length;
    const write = planAccountWrite({}, {}, { layouts }, size - 10)!;

    expect(write.leftOut).toEqual(['l19']);
    expect(ids(write.patch.layouts)).toEqual(ids(layouts.slice(0, 19)));
  });

  it('never leaves out a layout the account already holds to make room for one added here', () => {
    const held = Array.from({ length: 10 }, (_, n) => layout(n));
    const stored = { layouts: held };
    const size = JSON.stringify(stored).length;
    const base = accountPrefsIn(stored);

    const write = planAccountWrite(stored, base, { layouts: [...held, layout(99)] }, size + 5)!;

    expect(write.leftOut).toEqual(['l99']);
    // Nothing of the account's is touched: the layouts it holds are what it is sent.
    expect(applyMergePatch(stored, write.patch)).toEqual(stored);
  });

  it('counts the document as the server does, a character outside the basic plane as two escapes', () => {
    // A Romanian letter is one character either way; an emoji is two to JSON.stringify and the
    // twelve characters of two `\uXXXX` escapes to the server.
    expect(preferencesLength({ n: 'ț' })).toBe(JSON.stringify({ n: 'ț' }).length);
    expect(preferencesLength({ n: '🦇' })).toBe(JSON.stringify({ n: '' }).length + 12);

    const named = (n: number) => ({ id: `l${n}`, name: '🦇'.repeat(40) });
    const layouts = [named(1), named(2)];
    // Room for one such layout as the server counts it — and, as JSON.stringify counts, for both.
    const budget = preferencesLength({ layouts: [named(1)] });
    expect(JSON.stringify({ layouts }).length).toBeLessThan(budget);
    expect(planAccountWrite({}, {}, { layouts }, budget)!.leftOut).toEqual(['l2']);
  });

  it('gives up when there are no layouts of its own left to leave out', () => {
    const selectors = { x: { presentation: 'y'.repeat(PREFERENCES_MAX_LENGTH) } };
    expect(planAccountWrite({}, {}, { selectors })).toBeNull();
  });

  it('holds a realistic full workspace well inside the server limit', () => {
    // Every account preference set, at the sizes a heavy user reaches: three panels arranged, six
    // named layouts, a dozen selectors, a dozen pinned types, every quick filter pinned.
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
      pinnedFilterIds: Array.from(
        { length: MAX_PINNED_FILTERS },
        (_, n) => `01a16a67-8718-72e3-b214-${n.toString(16).padStart(12, '0')}`,
      ),
      coordinateFormat: 'utm',
      overviewMap: true,
    });

    expect(JSON.stringify(full).length).toBeLessThan(PREFERENCES_MAX_LENGTH);
  });
});

describe('the account preference list', () => {
  it('is the list the store resets, with its defaults', () => {
    expect([...ACCOUNT_PREF_KEYS].sort()).toEqual(Object.keys(accountPrefDefaults()).sort());
    expect(ACCOUNT_PREF_KEYS).toContain('karstLinkTreatment');
  });

  it('writes a remembered answer about protected positions to the account, and reads it back', () => {
    expect(pickAccountPrefs({ ...ACCOUNT_PREF_DEFAULTS, karstLinkTreatment: 'omit' })).toEqual({ karstLinkTreatment: 'omit' });
    expect(accountPrefsIn({ karstLinkTreatment: 'grid_position' })).toEqual({ karstLinkTreatment: 'grid_position' });
    // Only one of the three words: anything else is an answer this client cannot honour.
    expect(accountPrefsIn({ karstLinkTreatment: 'surveyed' })).toEqual({});
  });

  it('keeps a new account sharing filters with nobody, and writes nothing for that default', () => {
    expect(ACCOUNT_PREF_DEFAULTS.savedFilterAudience).toEqual({ visibility: 'private' });
    expect(pickAccountPrefs({ ...ACCOUNT_PREF_DEFAULTS })).not.toHaveProperty('savedFilterAudience');
    expect(accountPrefsIn({})).not.toHaveProperty('savedFilterAudience');
  });

  it('writes a chosen default audience for saved filters to the account, and reads it back', () => {
    const club = { visibility: 'cavingGroup', cavingGroupId: 'g-2' } as const;
    expect(pickAccountPrefs({ ...ACCOUNT_PREF_DEFAULTS, savedFilterAudience: club })).toEqual({ savedFilterAudience: club });
    expect(accountPrefsIn({ savedFilterAudience: club })).toEqual({ savedFilterAudience: club });
    expect(accountPrefsIn({ savedFilterAudience: { visibility: 'authenticated' } })).toEqual({
      savedFilterAudience: { visibility: 'authenticated' },
    });
  });

  it('never takes a stored audience it cannot honour as the default', () => {
    // `public` is refused by the server for a saved filter; a group with no id names nobody; a
    // private audience naming a group is the very shape the server refuses on save.
    expect(accountPrefsIn({ savedFilterAudience: { visibility: 'public' } })).toEqual({});
    expect(accountPrefsIn({ savedFilterAudience: { visibility: 'cavingGroup' } })).toEqual({});
    expect(accountPrefsIn({ savedFilterAudience: { visibility: 'private', cavingGroupId: 'g-2' } })).toEqual({});
    expect(accountPrefsIn({ savedFilterAudience: 'authenticated' })).toEqual({});
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
        coordinateFormat: 'gauss-kruger',
        pinnedTypeIds: [1, 2],
        overviewMap: 'yes',
        docks: { leftHidden: true },
      }),
    ).toEqual({ pinnedTypeIds: [1, 2], docks: { leftHidden: true, rightHidden: false } });
  });

  it('keeps pinned saved filters only as ids, once each and no more than the chip row holds', () => {
    const id = (n: number) => `00000000-0000-0000-0000-${String(n).padStart(12, '0')}`;
    expect(sanitizeAccountPrefs({ pinnedFilterIds: [id(1), 'not-an-id'] })).toEqual({});
    expect(sanitizeAccountPrefs({ pinnedFilterIds: 'everything' })).toEqual({});
    const many = Array.from({ length: MAX_PINNED_FILTERS + 1 }, (_, n) => id(n + 1));
    expect(sanitizeAccountPrefs({ pinnedFilterIds: [id(1), id(1).toUpperCase(), ...many.slice(1)] })).toEqual({
      pinnedFilterIds: many.slice(0, MAX_PINNED_FILTERS),
    });
    expect(accountPrefsIn({ pinnedFilterIds: [id(9)] })).toEqual({ pinnedFilterIds: [id(9)] });
    expect(ACCOUNT_PREF_KEYS).toContain('pinnedFilterIds');
  });

  it('keeps the good members of the coordinate settings and drops only what it cannot use', () => {
    const out = sanitizeAccountPrefs({
      coordinateFormat: 'stereo70',
      coordinateSettings: { dd: { decimals: 3, style: 'nonsense' }, utm: { zone: 35, precisionMetres: 7 }, unknown: {} },
      coordinateFormatsListed: ['mgrs', 'nonsense', 'dd', 'mgrs'],
    });
    expect(out.coordinateFormat).toBe('stereo70');
    expect(out.coordinateSettings?.dd).toEqual({ decimals: 3, style: 'letters' });
    expect(out.coordinateSettings?.utm).toEqual({ zone: 35, precisionMetres: 1 });
    expect(out.coordinateSettings?.pluscode).toEqual({ length: 10 });
    expect(out.coordinateFormatsListed).toEqual(['mgrs', 'dd']);
  });

  it('keeps the reading rules member by member, and writes nothing while they are the defaults', () => {
    expect(sanitizeAccountPrefs({ coordinateReading: { pairOrder: 'lonLat', bareUtmHemisphere: 'east' } }).coordinateReading)
      .toEqual({ pairOrder: 'lonLat', bareUtmHemisphere: 'north' });
    expect(accountPrefsIn({ coordinateReading: { pairOrder: 'latLon', bareUtmHemisphere: 'north' } })).toEqual({});
    expect(accountPrefsIn({ coordinateReading: { bareUtmHemisphere: 'south' } })).toEqual({
      coordinateReading: { pairOrder: 'latLon', bareUtmHemisphere: 'south' },
    });
    expect(ACCOUNT_PREF_KEYS).toContain('coordinateReading');
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

describe('planLeavingWrite: what a closing page sends without reading first', () => {
  const base: AccountPrefs = {
    coordinateFormat: 'dms',
    panels: { main: { width: 30 } },
    layouts: [{ id: 'a', name: 'Survey', panels: {} }],
  };

  it('names only what was changed here, member by member', () => {
    const local: AccountPrefs = { ...base, coordinateFormat: 'utm', panels: { main: { width: 30 }, popout: { pinned: false } } };
    expect(planLeavingWrite(base, local)).toEqual({ coordinateFormat: 'utm', panels: { popout: { pinned: false } } });
  });

  it('removes a preference put back to its default', () => {
    const local: AccountPrefs = { panels: base.panels, layouts: base.layouts };
    expect(planLeavingWrite(base, local)).toEqual({ coordinateFormat: null });
  });

  it('never sends the named layouts, which only a write that reads first may merge', () => {
    const local: AccountPrefs = { ...base, layouts: [...(base.layouts as object[]), { id: 'b', name: 'Late', panels: {} }] };
    expect(planLeavingWrite(base, local)).toBeNull();
    expect(planLeavingWrite(base, { ...local, overviewMap: true })).toEqual({ overviewMap: true });
  });

  it('sends nothing when nothing changed, or when the result would not fit', () => {
    expect(planLeavingWrite(base, base)).toBeNull();
    expect(planLeavingWrite(base, { ...base, coordinateFormat: 'utm' }, 10)).toBeNull();
  });
});

describe('baselineAfterLeaving', () => {
  it('takes what a closing page sent as written, leaving only an unsent layout change still to go', () => {
    const base = accountPrefsIn({ coordinateFormat: 'dms', overviewMap: true });
    const local: AccountPrefs = { ...base, coordinateFormat: 'utm' };
    delete local.overviewMap;
    const patch = planLeavingWrite(base, local);
    expect(patch).toEqual({ coordinateFormat: 'utm', overviewMap: null });
    const after = baselineAfterLeaving(base, patch!);
    expect(changedSince(after, local)).toBe(false);
    expect(changedSince(base, local)).toBe(true);
  });
});

describe('layoutsNotIn', () => {
  it('lists the layouts held here that the account does not hold', () => {
    const account: AccountPrefs = { layouts: [{ id: 'a' }] };
    expect(layoutsNotIn(account, { layouts: [{ id: 'a' }, { id: 'b' }] })).toEqual(['b']);
    expect(layoutsNotIn({}, {})).toEqual([]);
  });
});
