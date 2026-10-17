// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import {
  DEFAULT_SAVED_FILTER_AUDIENCE,
  audienceFields,
  audienceFromKey,
  audienceKey,
  effectiveSavedFilterAudience,
  isSavedFilterAudience,
  type SavedFilterAudience,
} from './savedFilterAudience.ts';

const club: SavedFilterAudience = { visibility: 'cavingGroup', cavingGroupId: 'g-1' };

describe('a saved filter audience', () => {
  it('is private when nothing was chosen', () => {
    expect(DEFAULT_SAVED_FILTER_AUDIENCE).toEqual({ visibility: 'private' });
  });

  it('accepts the three audiences a member may choose and nothing else', () => {
    expect(isSavedFilterAudience({ visibility: 'private' })).toBe(true);
    expect(isSavedFilterAudience({ visibility: 'authenticated' })).toBe(true);
    expect(isSavedFilterAudience(club)).toBe(true);

    expect(isSavedFilterAudience({ visibility: 'public' })).toBe(false);
    expect(isSavedFilterAudience({ visibility: 'cavingGroup', cavingGroupId: '' })).toBe(false);
    expect(isSavedFilterAudience({ visibility: 'authenticated', cavingGroupId: 'g-1' })).toBe(false);
    expect(isSavedFilterAudience(null)).toBe(false);
    expect(isSavedFilterAudience(['private'])).toBe(false);
  });

  it('keeps a club default while the member still belongs to it, and falls back to private once not', () => {
    expect(effectiveSavedFilterAudience(club, [{ id: 'g-0' }, { id: 'g-1' }])).toEqual(club);
    // Left the club, or it was removed: a stale default only narrows.
    expect(effectiveSavedFilterAudience(club, [{ id: 'g-0' }])).toEqual({ visibility: 'private' });
    expect(effectiveSavedFilterAudience(club, [])).toEqual({ visibility: 'private' });
    // Still loading: the stored choice stands; the server refuses a group the member may not use.
    expect(effectiveSavedFilterAudience(club, undefined)).toEqual(club);
    expect(effectiveSavedFilterAudience({ visibility: 'authenticated' }, [])).toEqual({ visibility: 'authenticated' });
  });

  it('becomes the two fields a write sends, naming a group only for a club', () => {
    expect(audienceFields({ visibility: 'private' })).toEqual({ visibility: 'private', cavingGroupId: null });
    expect(audienceFields({ visibility: 'authenticated' })).toEqual({ visibility: 'authenticated', cavingGroupId: null });
    expect(audienceFields(club)).toEqual({ visibility: 'cavingGroup', cavingGroupId: 'g-1' });
  });

  it('round-trips through a choice control value', () => {
    for (const audience of [DEFAULT_SAVED_FILTER_AUDIENCE, { visibility: 'authenticated' } as const, club]) {
      expect(audienceFromKey(audienceKey(audience))).toEqual(audience);
    }
    expect(audienceFromKey('public')).toEqual({ visibility: 'private' });
    expect(audienceFromKey('group:')).toEqual({ visibility: 'private' });
  });
});
