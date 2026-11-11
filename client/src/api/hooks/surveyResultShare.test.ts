// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { surveyResultShare as shareOf } from './surveyUploadViewer.ts';

/** What "Copy link" copies on the signed-in page of results. */
describe('the copied link', () => {
  const statusPageUrl = '/caves/c/surveys/s/uploads/u';

  it('is the public link\'s page when the upload has a public link', () => {
    const share = shareOf(
      {
        statusPageUrl,
        publicLink: { url: '/p/survey/tok' },
      },
      'https://silex.example',
    );
    expect(share).toEqual({ url: 'https://silex.example/p/survey/tok/view', kind: 'public' });
  });

  it('is this page in the application when it has none', () => {
    expect(shareOf({ statusPageUrl, publicLink: null }, 'https://silex.example')).toEqual({
      url: 'https://silex.example/caves/c/surveys/s/uploads/u/view',
      kind: 'inApp',
    });
  });
});
