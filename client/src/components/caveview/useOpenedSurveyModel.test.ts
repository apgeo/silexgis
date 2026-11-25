// SPDX-License-Identifier: AGPL-3.0-or-later
import { act, renderHook } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';
import type { SurveyModelInfo } from '../../api/hooks.ts';

/** What the model's own query answers for each id, as it would after its latest refetch. */
let answers: Record<string, SurveyModelInfo | undefined> = {};
const askedFor: (string | undefined)[] = [];
vi.mock('../../api/hooks.ts', () => ({
  useSurveyModel: (id: string | undefined) => {
    askedFor.push(id);
    return { data: id === undefined ? undefined : answers[id] };
  },
}));

const { useOpenedSurveyModel } = await import('./useOpenedSurveyModel.ts');

const signed = (id: string, token: string) =>
  ({ id, name: id, modelUrl: `/api/v1/files/${id}/content?token=${token}` }) as SurveyModelInfo;

beforeEach(() => {
  answers = {};
  askedFor.length = 0;
});

describe('a survey model opened in a dialog', () => {
  it('shows the address its query signed last, not the one it was opened with', () => {
    answers = { m1: signed('m1', 'first') };
    const { result, rerender } = renderHook(() => useOpenedSurveyModel());
    expect(result.current.model).toBeNull();

    act(() => result.current.open(signed('m1', 'first')));
    expect(result.current.model?.modelUrl).toContain('token=first');

    // The query asked again before the first signature lapsed.
    answers = { m1: signed('m1', 'second') };
    rerender();
    expect(result.current.model?.modelUrl).toContain('token=second');
  });

  it('closes when told to, and over a model its query no longer answers for', () => {
    answers = { m1: signed('m1', 'first') };
    const { result, rerender } = renderHook(() => useOpenedSurveyModel());
    act(() => result.current.open(signed('m1', 'first')));
    expect(result.current.model).not.toBeNull();

    // Withheld or deleted since: the query has nothing, so neither has the dialog.
    answers = {};
    rerender();
    expect(result.current.model).toBeNull();

    answers = { m1: signed('m1', 'first') };
    rerender();
    act(() => result.current.close());
    expect(result.current.model).toBeNull();
    expect(askedFor.at(-1)).toBeUndefined();
  });
});
