// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import i18n from '../i18n';
import { CaveViewBundleError } from './loadCaveView.ts';
import { SURVEY_FILE_STALL_MS, SurveyFileRequestError, SurveyFileStalledError } from './surveyFileDownload.ts';
import { caveViewerFailure, caveViewerFailureMessage, type CaveViewerFailure } from './viewerLifecycle.ts';

describe('what a failed viewer load is told to the reader as', () => {
  it.each([
    [new SurveyFileStalledError(), { kind: 'stalled' }],
    [new CaveViewBundleError('failed to load the CaveView bundle'), { kind: 'viewer' }],
    [new SurveyFileRequestError(404), { kind: 'missing' }],
    [new SurveyFileRequestError(410), { kind: 'missing' }],
    [new SurveyFileRequestError(401), { kind: 'refused' }],
    [new SurveyFileRequestError(403), { kind: 'refused' }],
    [new SurveyFileRequestError(502), { kind: 'server', status: 502 }],
    [new TypeError('Failed to fetch'), { kind: 'network' }],
    ['not even an error', { kind: 'network' }],
  ] as const)('reads %s as %o', (error, failure) => {
    expect(caveViewerFailure(error)).toEqual(failure);
  });

  const every: CaveViewerFailure[] = [
    { kind: 'unreadable' },
    { kind: 'stalled' },
    { kind: 'missing' },
    { kind: 'refused' },
    { kind: 'server', status: 502 },
    { kind: 'viewer' },
    { kind: 'network' },
  ];

  it.each(['en', 'ro'])('says every failure in a sentence of its own in %s, never the raw error', (language) => {
    const t = i18n.getFixedT(language);
    const sentences = every.map((failure) => caveViewerFailureMessage(failure, t));
    for (const sentence of sentences) {
      // A missing key comes back as the key itself.
      expect(sentence).not.toMatch(/^caveview\./);
      expect(sentence).not.toMatch(/survey file request failed|stopped arriving/);
    }
    expect(new Set(sentences).size).toBe(sentences.length);
    // The numbers the reader is owed are in the sentence, not left as placeholders.
    expect(sentences[1]).toContain(String(Math.round(SURVEY_FILE_STALL_MS / 1000)));
    expect(sentences[4]).toContain('502');
    expect(sentences.join(' ')).not.toContain('{{');
  });
});
