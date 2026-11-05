// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { ApiError } from '../client.ts';
import {
  isUnknownPublicLink,
  publicResultView,
  surveyUploadPublicLinkRefusal,
  type PublicSurveyUpload,
} from './surveyUploadPublicLinks.ts';

const envelope = (over: Partial<PublicSurveyUpload> = {}): PublicSurveyUpload => ({
  survey: { title: 'Invented survey' },
  cave: { name: 'Invented cave' },
  version: '2026-10-25T01:32:00Z',
  status: 'succeeded',
  stage: 'unpack',
  stages: [],
  progress: 100,
  message: 'Archived and read.',
  error: null,
  artefacts: [],
  logTail: null,
  log: null,
  createdAt: '2026-10-25T01:32:00Z',
  finishedAt: '2026-10-25T01:33:00Z',
  ...over,
});

describe('publicResultView', () => {
  it('carries the compiler’s log tail and the whole log’s address through to the page', () => {
    const view = publicResultView(
      envelope({
        logTail: 'error: invented failure',
        log: { name: 'invented.log', sizeBytes: 12, url: '/api/v1/public/survey-uploads/abc/log' },
      }),
    );
    expect(view.logTail).toBe('error: invented failure');
    expect(view.log).toEqual({ name: 'invented.log', sizeBytes: 12, url: '/api/v1/public/survey-uploads/abc/log' });
    expect(publicResultView(envelope()).log).toBeNull();
  });

  it('reads the title, the cave name and the progress off the envelope', () => {
    const view = publicResultView(envelope());
    expect(view).toMatchObject({ title: 'Invented survey', caveName: 'Invented cave', progress: 100, status: 'succeeded' });
  });

  it('works the stage states out from stage and status when the answer names no stages', () => {
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

describe('surveyUploadPublicLinkRefusal', () => {
  it('reads every code the server writes, and nothing else', () => {
    for (const code of ['installation-off', 'account-off', 'no-share-right', 'protected-cave', 'revoked']) {
      expect(surveyUploadPublicLinkRefusal({ publicLinkRefusal: code })).toBe(code);
    }
    expect(surveyUploadPublicLinkRefusal({ publicLinkRefusal: 'a-later-reason' })).toBeNull();
    expect(surveyUploadPublicLinkRefusal({ publicLinkRefusal: null })).toBeNull();
  });
});

describe('isUnknownPublicLink', () => {
  it('is true only for a 404, which is how a revoked and an invented link both answer', () => {
    expect(isUnknownPublicLink(new ApiError(404))).toBe(true);
    expect(isUnknownPublicLink(new ApiError(500))).toBe(false);
    expect(isUnknownPublicLink(new Error('network'))).toBe(false);
  });
});
