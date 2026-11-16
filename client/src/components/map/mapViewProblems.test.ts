// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import { mapViewProblem } from './mapViewProblems.ts';

const t = (key: string) => key;

describe('mapViewProblem', () => {
  it('words each refusal the saved-view routes give', () => {
    expect(mapViewProblem(new ApiError(403, 'access.create_forbidden'), t)).toBe('views.problems.createForbidden');
    expect(mapViewProblem(new ApiError(403, 'access.caving_group_binding_forbidden'), t)).toBe(
      'views.problems.groupForbidden',
    );
    expect(mapViewProblem(new ApiError(400, 'saved_filter.anchor_refused'), t)).toBe('views.filterHoldsPosition');
    expect(mapViewProblem(new ApiError(404, 'map_view.not_found'), t, 'common.deleteFailed')).toBe(
      'views.problems.notFound',
    );
    expect(mapViewProblem(new ApiError(400, 'validation.failed'), t)).toBe('views.problems.invalid');
  });

  it('words an uncoded refusal by its status, and anything else as the plain failure', () => {
    expect(mapViewProblem(new ApiError(403), t)).toBe('views.problems.forbidden');
    expect(mapViewProblem(new ApiError(404), t)).toBe('views.problems.notFound');
    expect(mapViewProblem(new ApiError(503), t, 'common.deleteFailed')).toBe('common.deleteFailed');
    expect(mapViewProblem(new Error('offline'), t)).toBe('common.saveFailed');
  });
});
