// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient } from '@tanstack/react-query';
import { afterEach, describe, expect, it, vi } from 'vitest';
import i18n from '../i18n';
import { ApiError } from '../api/client.ts';
import type { ResourceRef } from './resourceRef.ts';

const geometryFor = vi.fn();
vi.mock('./geoTargets.ts', () => ({ geometryFor: (...args: unknown[]) => geometryFor(...args) }));

const { followReveal, REVEAL_PROBLEM_KEYS, revealProblemKey } = await import('./reveal.ts');

const ref = { targetType: 'feature', targetId: 'f-1' } as unknown as ResourceRef;

afterEach(() => {
  geometryFor.mockReset();
});

describe('revealProblemKey', () => {
  it('names a link to something gone, something hidden, and a read that did not land apart', () => {
    expect(revealProblemKey(new ApiError(404, 'feature.not_found'))).toBe(REVEAL_PROBLEM_KEYS.gone);
    expect(revealProblemKey(new ApiError(410))).toBe(REVEAL_PROBLEM_KEYS.gone);
    expect(revealProblemKey(new ApiError(403, 'acl.forbidden'))).toBe(REVEAL_PROBLEM_KEYS.forbidden);
    expect(revealProblemKey(new ApiError(401))).toBe(REVEAL_PROBLEM_KEYS.forbidden);
    expect(revealProblemKey(new ApiError(503))).toBe(REVEAL_PROBLEM_KEYS.failed);
    expect(revealProblemKey(new TypeError('Failed to fetch'))).toBe(REVEAL_PROBLEM_KEYS.failed);
  });

  it('holds every sentence in both languages', () => {
    for (const key of Object.values(REVEAL_PROBLEM_KEYS)) {
      expect(i18n.exists(key, { lng: 'en' }), `${key} in English`).toBe(true);
      expect(i18n.exists(key, { lng: 'ro' }), `${key} in Romanian`).toBe(true);
    }
  });
});

describe('followReveal', () => {
  it('frames what the reference names once its position is read', async () => {
    const target = { geometry: { type: 'Point', coordinates: [23, 45] }, approximate: false };
    geometryFor.mockResolvedValue(target);
    const frame = vi.fn();
    const problem = vi.fn();
    followReveal(new QueryClient(), ref, frame, problem);
    await vi.waitFor(() => expect(frame).toHaveBeenCalledWith(target));
    expect(problem).not.toHaveBeenCalled();
  });

  // A link to a feature deleted after the link was written, or one this reader may not see, is a
  // refused read. Left unhandled it is a rejection nobody catches and a link that silently does
  // nothing; handled, it is a sentence saying why the view stayed where it was.
  it('turns a refused read into the sentence that says why, and frames nothing', async () => {
    geometryFor.mockRejectedValue(new ApiError(404, 'feature.not_found'));
    const frame = vi.fn();
    const problem = vi.fn();
    followReveal(new QueryClient(), ref, frame, problem);
    await vi.waitFor(() => expect(problem).toHaveBeenCalledWith(REVEAL_PROBLEM_KEYS.gone));
    expect(frame).not.toHaveBeenCalled();
  });
});
