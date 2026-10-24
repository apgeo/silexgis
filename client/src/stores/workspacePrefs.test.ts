// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { accountPrefsIn, pickAccountPrefs, sanitizeAccountPrefs } from './accountPrefs.ts';
import { DEFAULT_ARRANGEMENT, sanitizeArrangement, sanitizeSavedWorkspace } from './workspacePrefs.ts';

describe('the workspace arrangement as stored', () => {
  it('is taken member by member, each unknown value falling back to its default', () => {
    expect(
      sanitizeArrangement({ scene3dPlace: 'bottom', caveViewPlace: 'floating', table: 'yes', docksSwapped: true }),
    ).toEqual({ scene3dPlace: 'bottom', caveViewPlace: 'right', table: false, docksSwapped: true });
    expect(sanitizeArrangement({})).toEqual(DEFAULT_ARRANGEMENT);
    expect(sanitizeArrangement(null)).toBeUndefined();
    expect(sanitizeArrangement(['left'])).toBeUndefined();
  });

  it('keeps a strip height only within the bounds the map column leaves room for', () => {
    expect(sanitizeArrangement({ bottomSize: 40.4 })?.bottomSize).toBe(40);
    expect(sanitizeArrangement({ bottomSize: 90 })?.bottomSize).toBeUndefined();
    expect(sanitizeArrangement({ bottomSize: 5 })?.bottomSize).toBeUndefined();
    expect(sanitizeArrangement({ bottomSize: Number.NaN })?.bottomSize).toBeUndefined();
  });

  it("travels with the account's preferences once somebody has arranged anything, and not before", () => {
    expect(pickAccountPrefs(sanitizeAccountPrefs({}) as Record<string, unknown>)).toEqual({});
    // The arrangement as shipped, chosen, is still a choice: it is what stops an installation's
    // starting preset being applied to an account that has arranged its workspace.
    expect(accountPrefsIn({ workspace: DEFAULT_ARRANGEMENT })).toEqual({ workspace: DEFAULT_ARRANGEMENT });
    expect(accountPrefsIn({ workspace: 'map' })).toEqual({});
  });
});

describe("a saved layout's workspace", () => {
  it('is applied only when it holds an arrangement', () => {
    expect(sanitizeSavedWorkspace(undefined)).toBeUndefined();
    expect(sanitizeSavedWorkspace({ docks: { leftHidden: true } })).toBeUndefined();
    expect(
      sanitizeSavedWorkspace({ arrangement: { table: true }, docks: { leftHidden: true }, scene3dOpen: 'yes' }),
    ).toEqual({
      arrangement: { ...DEFAULT_ARRANGEMENT, table: true },
      docks: { leftHidden: true, rightHidden: false },
      scene3dOpen: false,
    });
  });
});
