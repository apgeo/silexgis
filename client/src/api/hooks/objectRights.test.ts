// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { objectRightsFrom } from './objectRights.ts';

describe('objectRightsFrom', () => {
  it('stands in with the installation-wide right until the object has answered', () => {
    expect(objectRightsFrom(undefined, true)).toMatchObject({ canEdit: true, canDelete: true, settled: false });
    expect(objectRightsFrom(undefined, false)).toMatchObject({ canEdit: false, canShare: false, settled: false });
  });

  it('follows the object once it has answered, whatever the installation-wide right', () => {
    // Held on this object only: no installation-wide write, but this cave was given.
    expect(objectRightsFrom('read, write, share', false)).toEqual({
      canEdit: true,
      canDelete: false,
      canShare: true,
      canManagePermissions: false,
      settled: true,
    });
    // Held installation-wide, but withheld on this object.
    expect(objectRightsFrom('read', true)).toEqual({
      canEdit: false,
      canDelete: false,
      canShare: false,
      canManagePermissions: false,
      settled: true,
    });
  });

  it('reads an answer of no actions as no rights, not as no answer', () => {
    expect(objectRightsFrom('', true).canEdit).toBe(false);
    expect(objectRightsFrom('none', true).canEdit).toBe(false);
  });
});
