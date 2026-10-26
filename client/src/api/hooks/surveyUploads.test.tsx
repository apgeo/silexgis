// SPDX-License-Identifier: AGPL-3.0-or-later
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook } from '@testing-library/react';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { queryKeys } from './queryKeys.ts';
import {
  groupCaveSurveys,
  normaliseAlwaysOutputs,
  surveyUploadLinkHref,
  surveyUploadListPollInterval,
  surveyUploadPollInterval,
  surveyUploadStageStates,
  useRefreshCaveWhenUploadSettles,
  type StageState,
  type SurveyUpload,
  type SurveyUploadListItem,
  type SurveyUploadStatus,
} from './surveyUploads.ts';

const statesOf = (input: Parameters<typeof surveyUploadStageStates>[0]): StageState[] =>
  surveyUploadStageStates(input).map((s) => s.state);

describe('surveyUploadPollInterval', () => {
  it('keeps asking while the upload is unfinished and never afterwards', () => {
    for (const status of ['receiving', 'queued', 'running'] as const) {
      expect(surveyUploadPollInterval(status)).toBe(2000);
    }
    for (const status of ['succeeded', 'failed', 'abandoned'] as const) {
      expect(surveyUploadPollInterval(status)).toBe(false);
    }
    expect(surveyUploadPollInterval(undefined)).toBe(false);
  });

  it('asks about a listing only while something on it is unfinished', () => {
    expect(surveyUploadListPollInterval([{ status: 'succeeded' }, { status: 'failed' }])).toBe(false);
    expect(surveyUploadListPollInterval([{ status: 'succeeded' }, { status: 'queued' }])).toBe(2000);
    expect(surveyUploadListPollInterval([])).toBe(false);
    expect(surveyUploadListPollInterval(undefined)).toBe(false);
  });
});

describe('surveyUploadStageStates', () => {
  it('lists the six stages in order', () => {
    expect(surveyUploadStageStates({ status: 'queued', stage: null }).map((s) => s.stage)).toEqual([
      'archive',
      'unpack',
      'scaffold',
      'compile',
      'store',
      'read',
    ]);
  });

  it('marks what is behind the running stage done and what is ahead pending', () => {
    expect(statesOf({ status: 'running', stage: 'compile' })).toEqual([
      'done', 'done', 'done', 'active', 'pending', 'pending',
    ]);
  });

  it('starts the first stage when a run has begun but not yet named one', () => {
    expect(statesOf({ status: 'running', stage: null })[0]).toBe('active');
  });

  it('marks the stage a failed run stopped at as failed', () => {
    expect(statesOf({ status: 'failed', stage: 'unpack' })).toEqual([
      'done', 'failed', 'pending', 'pending', 'pending', 'pending',
    ]);
  });

  it('does not claim stages a succeeded run never reached', () => {
    // Archived and read, not compiled: the run ends at unpack and reports 100 %.
    expect(statesOf({ status: 'succeeded', stage: 'unpack' })).toEqual([
      'done', 'done', 'skipped', 'skipped', 'skipped', 'skipped',
    ]);
    expect(statesOf({ status: 'succeeded', stage: 'read' })).toEqual(Array(6).fill('done'));
  });

  it('leaves everything waiting before the run starts and after an abandon', () => {
    expect(statesOf({ status: 'queued', stage: null })).toEqual(Array(6).fill('pending'));
    expect(statesOf({ status: 'receiving', stage: null })).toEqual(Array(6).fill('pending'));
    expect(statesOf({ status: 'abandoned', stage: null })).toEqual(Array(6).fill('pending'));
  });

  it('skips the scaffold and compile stages of a run not asked to compile, and only those', () => {
    expect(statesOf({ status: 'running', stage: 'unpack', compile: false })).toEqual([
      'done', 'active', 'skipped', 'skipped', 'pending', 'pending',
    ]);
    // Unknown is not "no": nothing is hidden for a guess.
    expect(statesOf({ status: 'queued', stage: null })[3]).toBe('pending');
  });
});

describe('normaliseAlwaysOutputs', () => {
  const catalogue = ['lox', '3d', 'plan-pdf', 'kml'];

  it('keeps catalogue order, once each, and drops names the installation does not produce', () => {
    expect(normaliseAlwaysOutputs(['kml', 'LOX', 'kml', 'dwg'], catalogue)).toEqual(['lox', 'kml']);
    expect(normaliseAlwaysOutputs([], catalogue)).toEqual([]);
  });
});

describe('surveyUploadLinkHref', () => {
  it('puts the installation address in front of the path the server writes', () => {
    expect(surveyUploadLinkHref('/p/survey/abc', 'https://sx.example.org')).toBe('https://sx.example.org/p/survey/abc');
  });

  it('leaves a complete address alone', () => {
    expect(surveyUploadLinkHref('https://other.example/p/survey/abc', 'https://sx.example.org')).toBe(
      'https://other.example/p/survey/abc',
    );
  });
});

function listItem(
  over: Partial<SurveyUploadListItem> & { id: string; surveyId: string; version: string },
): SurveyUploadListItem {
  const { surveyId, ...rest } = over;
  return {
    caveId: 'cave-1',
    caveName: 'Invented cave',
    caveSurvey: { id: surveyId, title: `Survey ${surveyId}` },
    source: 'topodroid',
    surveyTitle: `Survey ${surveyId}`,
    surveyName: null,
    surveyDate: null,
    uploadedBy: { id: 'u1', displayName: 'Ion A.' },
    status: 'succeeded',
    createdAt: over.version,
    archive: null,
    therionSources: [],
    statusPageUrl: `/caves/cave-1/surveys/${surveyId}/uploads/${over.id}`,
    publicLink: null,
    ...rest,
  } as SurveyUploadListItem;
}

describe('groupCaveSurveys', () => {
  it('groups uploads under their survey with versions newest first and surveys ordered by their newest version', () => {
    const groups = groupCaveSurveys([
      listItem({ id: 'a1', surveyId: 'A', version: '2026-10-01T10:00:00Z' }),
      listItem({ id: 'b1', surveyId: 'B', version: '2026-10-05T10:00:00Z' }),
      listItem({ id: 'a2', surveyId: 'A', version: '2026-10-03T10:00:00Z' }),
    ]);
    expect(groups.map((g) => g.id)).toEqual(['B', 'A']);
    const a = groups[1]!;
    expect(a.versions.map((v) => v.id)).toEqual(['a2', 'a1']);
    expect(a.versionCount).toBe(2);
    expect(a.newestVersionAt).toBe('2026-10-03T10:00:00Z');
  });

  it('keeps a failed upload as a version but leaves out sessions that never finished receiving or were abandoned', () => {
    const groups = groupCaveSurveys([
      listItem({ id: 'ok', surveyId: 'A', version: '2026-10-01T10:00:00Z' }),
      listItem({ id: 'bad', surveyId: 'A', version: '2026-10-02T10:00:00Z', status: 'failed' }),
      listItem({ id: 'rx', surveyId: 'A', version: '2026-10-04T10:00:00Z', status: 'receiving' }),
      listItem({ id: 'gone', surveyId: 'C', version: '2026-10-05T10:00:00Z', status: 'abandoned' }),
    ]);
    expect(groups).toHaveLength(1);
    expect(groups[0]!.versions.map((v) => v.id)).toEqual(['bad', 'ok']);
    expect(groups[0]!.versionCount).toBe(2);
  });

  it('answers nothing for nothing', () => {
    expect(groupCaveSurveys([])).toEqual([]);
  });
});

describe('useRefreshCaveWhenUploadSettles', () => {
  function setup(initial: SurveyUploadStatus | undefined) {
    const queryClient = new QueryClient();
    const invalidate = vi.spyOn(queryClient, 'invalidateQueries').mockResolvedValue();
    const wrapper = ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    );
    const upload = (status: SurveyUploadStatus | undefined) =>
      status ? ({ status, cave: { id: 'cave-1', name: 'x', url: '/caves/cave-1' } } as Pick<SurveyUpload, 'status' | 'cave'>) : undefined;
    const view = renderHook(({ status }) => useRefreshCaveWhenUploadSettles(upload(status)), {
      wrapper,
      initialProps: { status: initial },
    });
    return { invalidate, rerender: (status: SurveyUploadStatus | undefined) => view.rerender({ status }) };
  }

  it('refreshes the cave’s models, sources and surveys when a run being watched finishes', () => {
    const { invalidate, rerender } = setup('running');
    expect(invalidate).not.toHaveBeenCalled();
    rerender('succeeded');
    const keys = invalidate.mock.calls.map((c) => c[0]?.queryKey);
    expect(keys).toContainEqual(queryKeys.surveyModels('cave-1'));
    expect(keys).toContainEqual(queryKeys.surveySources('cave-1'));
    expect(keys).toContainEqual(queryKeys.caveSurveys('cave-1'));
  });

  it('asks for nothing on a page opened on an upload that was already finished', () => {
    const { invalidate, rerender } = setup(undefined);
    rerender('succeeded');
    rerender('succeeded');
    expect(invalidate).not.toHaveBeenCalled();
  });
});
