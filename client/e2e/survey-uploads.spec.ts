// SPDX-License-Identifier: AGPL-3.0-or-later
import { expect } from '@playwright/test';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';
import { archiveName, seedUpload } from './surveyUploads.ts';

/**
 * What a person sees of an upload a device sent: the status page of one run, and the cave page's
 * list of the surveys it holds.
 *
 * The upload is sent through the API as a device would, and runs against a server with no
 * compiler: it archives and reads the files, ends succeeded at a hundred per cent, and says in a
 * sentence that nothing was compiled. That ending is what these screens have to show faithfully.
 */
test.describe('survey uploads', () => {
  test('the status page shows a finished run, its stages, its files and the note that nothing was compiled', async ({
    page,
  }) => {
    await login(page);
    const title = `E2E Pestera ${Date.now()}`;
    const upload = await seedUpload(page, title);
    expect(upload.status).toBe('succeeded');

    await gotoRoute(page, upload.statusPageUrl);

    await expect(page.getByRole('heading', { name: title })).toBeVisible();
    await expect(page.getByTestId('survey-upload-status')).toHaveText('Finished');
    // A finished bar draws a tick, not the number; the value is on the element.
    await expect(page.getByTestId('survey-upload-percent')).toHaveAttribute('aria-valuenow', '100');
    // The note a server without a compiler leaves on a success; not an error.
    await expect(page.getByTestId('survey-upload-message')).toBeVisible();
    await expect(page.getByTestId('survey-upload-error')).toHaveCount(0);
    await expect(page.getByTestId('survey-upload-stage-archive')).toBeVisible();
    await expect(page.getByTestId('survey-upload-stage-read')).toBeVisible();
    await expect(page.getByTestId('survey-upload-no-artefacts')).toBeVisible();
    // The files sent, with a way to fetch each.
    await expect(page.getByRole('cell', { name: archiveName })).toBeVisible();
    // The public link has its own card whatever its state.
    await expect(page.getByTestId('survey-upload-link-card')).toBeVisible();
    // No way to delete a finished upload exists on the server, so none is offered.
    await expect(page.getByRole('button', { name: /Delete this upload/ })).toHaveCount(0);
  });

  test('a link is made from the status page for the audience chosen, and is marked with it', async ({ page }) => {
    await login(page);
    const upload = await seedUpload(page, `E2E Make Link ${Date.now()}`, { publicLink: { wanted: false } });
    expect(upload.publicLinkUrl, 'the upload asked for no link').toBeNull();

    await gotoRoute(page, upload.statusPageUrl);
    await expect(page.getByTestId('survey-upload-no-link')).toBeVisible();
    await page.getByTestId('survey-upload-link-make').click();
    await expect(page.getByTestId('survey-upload-link-dialog')).toBeVisible();
    // The cave this upload made is not protected, so nothing has to be acknowledged.
    await expect(page.getByTestId('survey-upload-link-acknowledge')).toHaveCount(0);
    await page.getByRole('radio', { name: /Signed-in accounts only/ }).check();
    await page.getByTestId('survey-upload-link-dialog-ok').click();

    await expect(page.getByTestId('survey-upload-link')).toContainText('/p/survey/');
    await expect(page.getByTestId('survey-upload-link-audience-tag')).toHaveText('Signed-in accounts only');
  });

  test('an address that names no upload says so', async ({ page, consoleErrors }) => {
    consoleErrors.allow(
      /Failed to load resource: .*404/,
      'an upload that does not exist is answered 404, which the page turns into its own sentence',
    );
    await login(page);
    await gotoRoute(page, '/caves/00000000-0000-0000-0000-000000000000/surveys/00000000-0000-0000-0000-000000000000/uploads/00000000-0000-0000-0000-000000000000');
    await expect(page.getByTestId('survey-upload-unavailable')).toBeVisible();
  });

  test('the cave page lists the survey, opens its version and shows what was sent', async ({ page }) => {
    await login(page);
    const title = `E2E Cave Surveys ${Date.now()}`;
    const upload = await seedUpload(page, title);

    await gotoRoute(page, `/caves/${upload.caveId}`);

    const section = page.getByTestId('cave-surveys');
    await expect(section).toBeVisible();
    const survey = section.locator('tr.ant-table-row').filter({ hasText: title });
    await expect(survey).toBeVisible();
    await survey.locator('.ant-table-row-expand-icon').click();

    const versions = page.getByTestId(`cave-survey-versions-${upload.caveSurveyId}`);
    await expect(versions).toBeVisible();
    await expect(versions.getByText('Finished')).toBeVisible();
    await versions.locator('.ant-table-row-expand-icon').click();
    await expect(page.getByTestId(`cave-survey-version-${upload.id}`).getByText(archiveName)).toBeVisible();

    // The existing archive of sources stays where it was.
    await expect(page.locator('.ant-card').filter({ hasText: 'Survey sources' })).toBeVisible();

    await versions.getByRole('link', { name: 'Status page' }).click();
    await expect(page).toHaveURL(new RegExp(`/uploads/${upload.id}$`));
    await expect(page.getByRole('heading', { name: title })).toBeVisible();
  });
});
