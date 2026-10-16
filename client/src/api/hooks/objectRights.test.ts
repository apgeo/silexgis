// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../client.ts';
import { retryUnlessRefused } from './shared.ts';
import { isSettledRefusal, objectRightsFrom, type InstallationWideRights } from './objectRights.ts';

const everything: InstallationWideRights = { write: true, delete: true, share: true, managePermissions: true };
const nothing: InstallationWideRights = { write: false, delete: false, share: false, managePermissions: false };

describe('objectRightsFrom', () => {
  it('stands in with the installation-wide right until the object has answered', () => {
    expect(objectRightsFrom(undefined, everything)).toMatchObject({ canEdit: true, canDelete: true, settled: false });
    expect(objectRightsFrom(undefined, nothing)).toMatchObject({ canEdit: false, canShare: false, settled: false });
  });

  it('follows the object once it has answered, whatever the installation-wide right', () => {
    // Held on this object only: no installation-wide write, but this cave was given.
    expect(objectRightsFrom('read, write, share', nothing)).toEqual({
      canEdit: true,
      canDelete: false,
      canShare: true,
      canManagePermissions: false,
      settled: true,
    });
    // Held installation-wide, but withheld on this object.
    expect(objectRightsFrom('read', everything)).toEqual({
      canEdit: false,
      canDelete: false,
      canShare: false,
      canManagePermissions: false,
      settled: true,
    });
  });

  it('reads an answer of no actions as no rights, not as no answer', () => {
    expect(objectRightsFrom('', everything).canEdit).toBe(false);
    expect(objectRightsFrom('none', everything).canEdit).toBe(false);
  });

  it('stands in with each right for itself: delete for Delete, not write', () => {
    expect(objectRightsFrom(undefined, { ...nothing, write: true })).toMatchObject({ canEdit: true, canDelete: false });
  });

  it('counts a refusal or a not-found as the object answering, and a failure to reach it as no answer', () => {
    expect(isSettledRefusal(new ApiError(403, 'forbidden'))).toBe(true);
    expect(isSettledRefusal(new ApiError(404, 'not_found'))).toBe(true);
    expect(isSettledRefusal(new ApiError(503, undefined))).toBe(false);
    expect(isSettledRefusal(new TypeError('Failed to fetch'))).toBe(false);
  });

  it('asks again for an answer that could not be had, and not for a refusal', () => {
    expect(retryUnlessRefused(0, new TypeError('Failed to fetch'))).toBe(true);
    expect(retryUnlessRefused(2, new ApiError(503, undefined))).toBe(true);
    expect(retryUnlessRefused(3, new ApiError(503, undefined))).toBe(false);
    expect(retryUnlessRefused(0, new ApiError(403, 'forbidden'))).toBe(false);
  });
});
