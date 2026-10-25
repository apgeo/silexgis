// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../client.ts';
import { isUnknownPublicLink, publicResultView, type PublicSurveyUploadEnvelope } from './surveyUploadPublicLinks.ts';

const envelope = (over: Partial<PublicSurveyUploadEnvelope> = {}): PublicSurveyUploadEnvelope => ({
  survey: { title: 'Invented survey' },
  cave: { name: 'Invented cave' },
  version: '2026-10-25T01:32:00Z',
  status: 'succeeded',
  stage: 'unpack',
  progress: 100,
  message: 'Archived and read.',
  error: null,
  artefacts: [],
  createdAt: '2026-10-25T01:32:00Z',
  finishedAt: '2026-10-25T01:33:00Z',
  ...over,
});

describe('publicResultView', () => {
  it('reads the title, the cave name and the progress off the envelope', () => {
    const view = publicResultView(envelope());
    expect(view).toMatchObject({ title: 'Invented survey', caveName: 'Invented cave', progress: 100, status: 'succeeded' });
  });

  it('works the stage states out from stage and status when the server states none', () => {
    expect(publicResultView(envelope()).stages.map((s) => s.state)).toEqual([
      'done', 'done', 'skipped', 'skipped', 'skipped', 'skipped',
    ]);
  });

  it('takes the server’s stage states when it sends them, reading an unknown state as pending', () => {
    const view = publicResultView(
      envelope({
        status: 'running',
        stage: 'compile',
        stages: [
          { stage: 'archive', state: 'done' },
          { stage: 'compile', state: 'active' },
          // A state a later server may add.
          { stage: 'store', state: 'retrying' as never },
        ],
      }),
    );
    expect(view.stages).toEqual([
      { stage: 'archive', state: 'done' },
      { stage: 'compile', state: 'active' },
      { stage: 'store', state: 'pending' },
    ]);
  });

  it('carries the error with its code and the artefacts untouched', () => {
    const view = publicResultView(
      envelope({
        status: 'failed',
        error: { code: 'survey_pipeline.compile_failed', message: 'Therion reported 2 errors' },
        artefacts: [{ kind: 'plan-pdf', name: 'x.pdf', sizeBytes: 10, contentUrl: '/api/v1/files/1/content?token=t' }],
      }),
    );
    expect(view.error?.code).toBe('survey_pipeline.compile_failed');
    expect(view.artefacts).toHaveLength(1);
  });
});

describe('isUnknownPublicLink', () => {
  it('is true only for a 404, which is how a revoked and an invented link both answer', () => {
    expect(isUnknownPublicLink(new ApiError(404))).toBe(true);
    expect(isUnknownPublicLink(new ApiError(500))).toBe(false);
    expect(isUnknownPublicLink(new Error('network'))).toBe(false);
  });
});
