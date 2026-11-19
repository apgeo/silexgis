// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../api/client.ts';

// The helper sits beside a hook whose own imports reach the network layer; none of that is
// exercised here, only the wording chosen for an error.
vi.mock('../../api/hooks/access.ts', () => ({ useCapabilities: vi.fn(), useCreateDoor: vi.fn() }));
vi.mock('../../api/hooks/people.ts', () => ({ useCavingGroups: vi.fn() }));

const { savedFilterSaveFailedKey } = await import('./useSavedFilterAudienceChoice.ts');

/**
 * Which sentence a refused save or update of a saved filter is told in. Pinned: a filter that is no
 * longer there is said to be gone rather than to need its conditions checked, a refusal the filter
 * editor already words by code keeps that sentence, only an unknown filter code falls back to the
 * general "cannot save it as it stands", and anything that is not a refusal is a failed save.
 */
describe('savedFilterSaveFailedKey', () => {
  it('says a filter deleted or unshared elsewhere is gone, not that its conditions need checking', () => {
    expect(savedFilterSaveFailedKey(new ApiError(404, 'saved_filter.not_found'))).toBe('savedFilters.saveRefused.gone');
  });

  it('keeps the filter editor\'s own sentence for a refusal it already words by code', () => {
    expect(savedFilterSaveFailedKey(new ApiError(400, 'filter.anchor_too_large'))).toBe(
      'filters.editor.spatial.refused.tooLarge',
    );
    expect(savedFilterSaveFailedKey(new ApiError(400, 'filter.malformed'))).toBe('filters.editor.wholeRefusal.malformed');
  });

  it('falls back to the general refusal only for a filter code nothing words yet', () => {
    expect(savedFilterSaveFailedKey(new ApiError(400, 'filter.invalid'))).toBe('savedFilters.saveRefused.notSavable');
    expect(savedFilterSaveFailedKey(new ApiError(400, 'saved_filter.document_required'))).toBe(
      'savedFilters.saveRefused.notSavable',
    );
  });

  it('names the specific refusals of the save itself', () => {
    expect(savedFilterSaveFailedKey(new ApiError(403, 'access.caving_group_binding_forbidden'))).toBe(
      'savedFilters.groupNotAllowed',
    );
    expect(savedFilterSaveFailedKey(new ApiError(403, 'access.create_forbidden'))).toBe('savedFilters.saveRefused.forbidden');
    expect(savedFilterSaveFailedKey(new ApiError(400, 'saved_filter.anchor_refused'))).toBe(
      'savedFilters.saveRefused.holdsPosition',
    );
  });

  it('tells what is not a refusal as a failed save', () => {
    expect(savedFilterSaveFailedKey(new TypeError('Failed to fetch'))).toBe('common.saveFailed');
    expect(savedFilterSaveFailedKey(new ApiError(500))).toBe('common.saveFailed');
  });
});
