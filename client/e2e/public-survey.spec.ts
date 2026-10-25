// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { ownContext, test } from './consoleGuard.ts';
import { login } from './helpers.ts';
import { seedUpload } from './surveyUploads.ts';

/**
 * The page behind a survey upload's public link, opened by somebody who is not signed in.
 *
 * The first test runs against a stubbed answer: that the address opens without a sign-in, that the
 * visitor is not sent to the login form, and that what the envelope carries is drawn. The stub
 * also carries members a public answer must never hold (a log, input files, a team); the page must
 * not draw them even if a server wrongly sent them. The second test drives the real route: an
 * upload sent the way a device sends one, its public link opened by a visitor without an account.
 */
test('the page behind a public link opens without signing in and draws the envelope it is given', async ({
  browser,
  consoleErrors,
}) => {
  consoleErrors.allow(
    /Failed to load resource: .*404/,
    'a token that names no link is answered 404, which the page turns into its own sentence',
  );
  const visitor = await ownContext(browser);
  try {
    const anonymous = await visitor.newPage();
    await anonymous.route('**/api/v1/public/survey-uploads/stub-token', (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          survey: { title: 'Invented stub survey' },
          cave: { name: 'Invented stub cave' },
          version: '2026-10-25T10:15:00Z',
          status: 'succeeded',
          stage: 'unpack',
          stages: [
            { stage: 'archive', state: 'done' },
            { stage: 'unpack', state: 'done' },
            { stage: 'scaffold', state: 'skipped' },
            { stage: 'compile', state: 'skipped' },
            { stage: 'store', state: 'skipped' },
            { stage: 'read', state: 'skipped' },
          ],
          progress: 100,
          message: 'Archived and read, not compiled.',
          error: null,
          artefacts: [{ kind: 'plan-pdf', name: 'plan.pdf', sizeBytes: 2048, contentUrl: '/files/stub/content' }],
          logTail: 'invented-log-line-that-must-stay-hidden',
          team: 'Invented Team Member',
          files: [{ name: 'invented-source-file.th', role: 'source' }],
          createdAt: '2026-10-25T10:15:00Z',
          finishedAt: '2026-10-25T10:15:03Z',
        }),
      }),
    );
    await anonymous.route('**/api/v1/public/survey-uploads/gone-token', (route) =>
      route.fulfill({ status: 404, contentType: 'application/problem+json', body: JSON.stringify({ status: 404, code: 'survey_upload.public_link_not_found' }) }),
    );

    await anonymous.goto('/p/survey/stub-token');
    await expect(anonymous.getByTestId('public-survey-result')).toBeVisible({ timeout: 30_000 });
    expect(new URL(anonymous.url()).pathname).toBe('/p/survey/stub-token');
    await expect(anonymous.getByRole('heading', { name: 'Invented stub survey' })).toBeVisible();
    await expect(anonymous.getByTestId('public-survey-cave')).toContainText('Invented stub cave');
    await expect(anonymous.getByTestId('survey-upload-status')).toHaveText('Finished');
    await expect(anonymous.getByText('plan.pdf')).toBeVisible();
    // Members the stubbed answer carries but a public view has no place for.
    await expect(anonymous.getByText('invented-log-line-that-must-stay-hidden')).toHaveCount(0);
    await expect(anonymous.getByText('Invented Team Member')).toHaveCount(0);
    await expect(anonymous.getByText('invented-source-file.th')).toHaveCount(0);

    await anonymous.goto('/p/survey/gone-token');
    await expect(anonymous.getByTestId('public-survey-unavailable')).toBeVisible({ timeout: 30_000 });
    expect(new URL(anonymous.url()).pathname).toBe('/p/survey/gone-token');
  } finally {
    await visitor.close();
  }
});

test.describe('the public result page, against the real route', () => {
  test('a link shows the run to a visitor without an account, and a wrong one says it does not work', async ({
    browser,
    page,
    consoleErrors,
  }) => {
    consoleErrors.allow(
      /Failed to load resource: .*404/,
      'a token that names no link is answered 404, which the page turns into its own sentence',
    );
    await login(page);
    const upload = await seedUpload(page, `E2E Public ${Date.now()}`);
    expect(upload.publicLinkUrl, 'a finished upload of an unprotected cave has a public link by default').toBeTruthy();

    const visitor = await ownContext(browser);
    try {
      const anonymous = await visitor.newPage();
      await anonymous.goto(new URL(upload.publicLinkUrl!, 'http://localhost').pathname);
      await expect(anonymous.getByTestId('public-survey-result')).toBeVisible({ timeout: 30_000 });
      await expect(anonymous.getByTestId('survey-upload-status')).toHaveText('Finished');
      await expect(anonymous.getByTestId('public-survey-cave')).toContainText(upload.caveName);

      await anonymous.goto('/p/survey/e2e-invented-token');
      await expect(anonymous.getByTestId('public-survey-unavailable')).toBeVisible({ timeout: 30_000 });
    } finally {
      await visitor.close();
    }
  });
});
