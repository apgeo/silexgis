// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../../api/client.ts';
import { featureWriteProblem } from './featureWriteProblems.ts';

const t = (key: string) => key;

describe('featureWriteProblem', () => {
  it('words a rule the server named', () => {
    expect(featureWriteProblem(new ApiError(400, 'feature.geometry_invalid'), t)).toBe(
      'mapEdit.problems.geometryInvalid',
    );
    expect(featureWriteProblem(new ApiError(409, 'feature.parent_primary_hidden'), t)).toBe(
      'mapEdit.problems.parentUnavailable',
    );
  });

  it('calls every concurrency refusal a change made by somebody else', () => {
    expect(featureWriteProblem(new ApiError(412, 'concurrency.version_mismatch'), t)).toBe('common.conflict');
    expect(featureWriteProblem(new ApiError(428, 'concurrency.if_match_required'), t)).toBe('common.conflict');
  });

  it('does not call a conflict it does not know a change made by somebody else', () => {
    expect(featureWriteProblem(new ApiError(409, 'feature.something_new'), t)).toBe('common.saveFailed');
  });

  it('falls back to the plain failure for anything that is not a refusal', () => {
    expect(featureWriteProblem(new Error('offline'), t, 'common.deleteFailed')).toBe('common.deleteFailed');
  });
});
