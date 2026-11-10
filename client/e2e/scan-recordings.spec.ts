// SPDX-License-Identifier: AGPL-3.0-or-later
import { mkdirSync, writeFileSync } from 'node:fs';
import path from 'node:path';
import { expect, type Page } from '@playwright/test';
import { asPerson, tryAsPerson } from './arrange.ts';
import { test } from './consoleGuard.ts';
import { gotoRoute, login } from './helpers.ts';

// A scanner's raw recording on its way into the product: the folder picked in the cave's
// point-cloud dialog, the files it will send and their size shown before anything is sent, the
// camera folder left out, an odometry profile preselected, and the upload's card describing what
// reading the recording found. The browser lane runs no scan worker, so the server archives and
// inspects the recording and stops where HDMapping's odometry would start, saying so; no cloud
// is made and the card offers no viewer. That stop is the state asserted here — the odometry
// itself is proved by the server's tests against a canned worker.

const HEADER = 'timestamp gyroX gyroY gyroZ accX accY accZ imuId timestampUnix\n';

/**
 * An invented LAZ chunk: the 227-byte LAS 1.2 public header the server reads, with the
 * compression bit set on its point format and `points` points declared, then a few bytes standing
 * in for the compressed points, which nothing in the lane decompresses. Nothing in it is a real scan.
 */
function tinyLaz(points: number): Buffer {
  const headerSize = 227;
  const buffer = Buffer.alloc(headerSize + 64);
  buffer.write('LASF', 0, 'ascii');
  buffer[24] = 1; // version major
  buffer[25] = 2; // version minor
  buffer.write('e2e recording', 58, 'ascii');
  buffer.writeUInt16LE(headerSize, 94);
  buffer.writeUInt32LE(headerSize, 96); // offset to point data
  buffer.writeUInt32LE(0, 100); // no variable-length records
  buffer[104] = 1 | 0x80; // point format 1, compressed
  buffer.writeUInt16LE(28, 105);
  buffer.writeUInt32LE(points, 107);
  for (const [at, value] of [
    [131, 0.01],
    [139, 0.01],
    [147, 0.01],
    [179, 2],
    [195, 2],
    [211, 1],
  ] as const) {
    buffer.writeDoubleLE(value, at);
  }
  return buffer;
}

/**
 * An invented recording folder on disk, in the scanner's layout: two lidar chunks with their IMU
 * logs (half a second and a second and a half, two seconds apart, so 2.5 s together) and the
 * serial numbers of two invented sensors, plus a camera folder with one placeholder picture,
 * which a recording upload leaves out.
 */
function writeRecording(folder: string): void {
  mkdirSync(path.join(folder, 'CAMERA_0'), { recursive: true });
  const files: [string, Buffer][] = [
    ['lidar0000.laz', tinyLaz(2)],
    ['imu0000.csv', Buffer.from(`${HEADER}1000000000 0.1 0.2 0.3 0 0 1 0 0\n1500000000 0.1 0.2 0.3 0 0 1 0 0\n`)],
    ['lidar0000.sn', Buffer.from('0 TESTSENSOR0001\n1 TESTSENSOR0002\n')],
    ['lidar0001.laz', tinyLaz(3)],
    ['imu0001.csv', Buffer.from(`${HEADER}2000000000 0.1 0.2 0.3 0 0 1 0 0\n3500000000 0.1 0.2 0.3 0 0 1 0 0\n`)],
    ['lidar0001.sn', Buffer.from('0 TESTSENSOR0001\n1 TESTSENSOR0002\n')],
  ];
  for (const [name, bytes] of files) {
    writeFileSync(path.join(folder, name), bytes);
  }
  writeFileSync(path.join(folder, 'CAMERA_0', 'frame0001.jpg'), Buffer.from('not a picture'));
}

const madeCaves: string[] = [];

test.afterEach(async ({ page }) => {
  for (const caveId of madeCaves.splice(0)) {
    await tryAsPerson(page, 'DELETE', `/api/v1/caves/${caveId}`);
  }
});

async function cave(page: Page, name: string): Promise<string> {
  const caveTypes = await asPerson<{ id: number }[]>(page, 'GET', '/api/v1/cave-types');
  const made = await asPerson<{ id: string }>(page, 'POST', '/api/v1/caves', {
    name,
    caveTypeId: caveTypes[0].id,
    visibility: 'authenticated',
    locationProtected: false,
    explorationStatus: 'Unknown',
    isShowCave: false,
  });
  madeCaves.push(made.id);
  return made.id;
}

test('a recording folder is picked on a cave, sent without its camera pictures and described on its card', async ({
  page,
}) => {
  test.setTimeout(180_000);
  const folder = test.info().outputPath('Galeria_e2e');
  writeRecording(folder);

  await login(page);
  const caveId = await cave(page, `E2E Recording ${Date.now()}`);
  await gotoRoute(page, `/caves/${caveId}`);
  await page.getByTestId('survey-model-upload-point-cloud').click();
  await page.getByTestId('point-cloud-kind').getByText('Scanner recording folder').click();

  // The folder as the picker hands it over: every file under it, the camera folder's included.
  // The upload passes its test id to its own file input, so the id names the input itself.
  await page.getByTestId('point-cloud-folder').setInputFiles(folder);
  const summary = page.getByTestId('point-cloud-recording-summary');
  await expect(summary).toBeVisible({ timeout: 15_000 });
  await expect(page.getByTestId('point-cloud-recording-count')).toHaveText('6');
  await expect(page.getByTestId('point-cloud-recording-skipped')).toHaveText('1');
  await expect(page.getByTestId('point-cloud-recording-files')).toContainText('lidar0001.laz');
  await expect(page.getByTestId('point-cloud-recording-files')).not.toContainText('frame0001.jpg');
  // An odometry profile is preselected (the one used last, else the built-in that changes nothing).
  await expect(page.getByTestId('point-cloud-profile')).toBeVisible();
  // The folder's name is offered as the survey's title.
  await expect(page.getByTestId('point-cloud-title')).toHaveValue('Galeria_e2e');

  const opening = page.waitForRequest(
    (request) => request.method() === 'POST' && new URL(request.url()).pathname === '/api/v1/survey-uploads',
  );
  await page.getByRole('button', { name: 'Upload recording' }).click();
  const declared = (await opening).postDataJSON() as {
    source: string;
    files: { name: string; role: string }[];
    profiles: string[];
  };
  // Declared as a recording, by bare file names (the server refuses a path), all as the archive.
  expect(declared.source).toBe('mandeye');
  expect(declared.files.map((file) => file.name).sort()).toEqual([
    'imu0000.csv',
    'imu0001.csv',
    'lidar0000.laz',
    'lidar0000.sn',
    'lidar0001.laz',
    'lidar0001.sn',
  ]);
  expect(declared.files.every((file) => file.role === 'archive')).toBe(true);
  expect(declared.profiles.length).toBeLessThanOrEqual(1);

  // The dialog hands over to the upload's own page once every file has arrived.
  await page.waitForURL(/\/surveys\/[^/]+\/uploads\/[^/]+$/, { timeout: 60_000 });
  const message = page.getByTestId('survey-upload-message');
  await expect(message).toContainText('no worker that runs HDMapping', { timeout: 90_000 });

  const card = page.getByTestId('point-cloud-card');
  await expect(card).toBeVisible();
  const facts = card.getByTestId('point-cloud-recording');
  await expect(facts.getByTestId('point-cloud-recording-chunks')).toHaveText('2');
  // 2.5 s of IMU log, said in whole seconds.
  await expect(facts).toContainText('3 seconds');
  await expect(facts.getByTestId('point-cloud-recording-gnss')).toHaveText('No');
  // No cloud was made, so there is nothing to look at: the card offers no viewer.
  await expect(card.getByTestId('point-cloud-view')).toHaveCount(0);

  // A profile sent on the upload is remembered for the person's next recording.
  if (declared.profiles.length === 1) {
    const remembered = await asPerson<{ stepId: string; profileId: string }[]>(
      page,
      'GET',
      '/api/v1/scan-profiles/remembered?stepId=hdmapping.odometry',
    );
    expect(remembered.map((choice) => choice.profileId)).toContain(declared.profiles[0]);
  }
});

/**
 * Sends the invented recording with its odometry to be done on another computer, and returns the
 * upload's id once the dialog has handed over to its page. The server accepts the choice; the
 * browser lane runs no scan processing, so its run still stops where the odometry would start.
 */
async function sendWithOdometryElsewhere(page: Page, folder: string): Promise<{ caveId: string; uploadId: string }> {
  await login(page);
  const caveId = await cave(page, `E2E Odometry elsewhere ${Date.now()}`);
  await gotoRoute(page, `/caves/${caveId}`);
  await page.getByTestId('survey-model-upload-point-cloud').click();
  await page.getByTestId('point-cloud-kind').getByText('Scanner recording folder').click();
  await page.getByTestId('point-cloud-folder').setInputFiles(folder);
  await expect(page.getByTestId('point-cloud-recording-summary')).toBeVisible({ timeout: 15_000 });
  await page.getByTestId('point-cloud-odometry-elsewhere').check();

  const opening = page.waitForRequest(
    (request) => request.method() === 'POST' && new URL(request.url()).pathname === '/api/v1/survey-uploads',
  );
  await page.getByRole('button', { name: 'Upload recording' }).click();
  expect(((await opening).postDataJSON() as { externalSteps: string[] }).externalSteps).toEqual(['hdmapping.odometry']);
  await page.waitForURL(/\/surveys\/[^/]+\/uploads\/[^/]+$/, { timeout: 60_000 });
  await expect(page.getByTestId('survey-upload-message')).toBeVisible({ timeout: 90_000 });
  return { caveId, uploadId: page.url().split('/').pop()! };
}

/**
 * Answers the upload's own status request with the server's real answer, changed by `change` — the
 * state a run reaches only with a scan worker, which the browser lane does not run. Everything
 * else the page asks goes to the server as it is.
 */
async function answerStatusWith(page: Page, uploadId: string, change: (dto: Record<string, unknown>) => void) {
  const real = await asPerson<Record<string, unknown>>(page, 'GET', `/api/v1/survey-uploads/${uploadId}`);
  change(real);
  await page.route(
    (url) => url.pathname === `/api/v1/survey-uploads/${uploadId}`,
    (route) =>
      route.request().method() === 'GET'
        ? route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify(real) })
        : route.fallback(),
  );
  return real;
}

test('a recording whose odometry is run elsewhere offers its inputs for download and takes the session back under its fixed name', async ({
  page,
}) => {
  test.setTimeout(180_000);
  const folder = test.info().outputPath('Elsewhere_e2e');
  writeRecording(folder);
  const { uploadId } = await sendWithOdometryElsewhere(page, folder);

  const waiting = await answerStatusWith(page, uploadId, (dto) => {
    dto.status = 'succeeded';
    dto.stage = 'awaitingExternal';
    dto.external = {
      stepId: 'hdmapping.odometry',
      stepName: 'LiDAR odometry',
      tool: 'hdmapping',
      outputs: [{ names: ['session.zip'], received: false }],
    };
  });
  // The bundle is the server's to build from a stopped run; here it stands in, so the download's
  // wiring — the upload's own route, with the bearer token, saved under the server's name — is what is checked.
  await page.route(
    (url) => url.pathname === `/api/v1/survey-uploads/${uploadId}/external/bundle`,
    (route) =>
      route.fulfill({
        status: 200,
        contentType: 'application/zip',
        headers: { 'Content-Disposition': 'attachment; filename="scan.hdmapping-odometry.zip"' },
        body: Buffer.from('PK\u0005\u0006' + '\u0000'.repeat(18), 'binary'),
      }),
  );
  let sentAs: string | undefined;
  await page.route(
    (url) => url.pathname.startsWith(`/api/v1/survey-uploads/${uploadId}/external/`) && !url.pathname.endsWith('/bundle'),
    (route) => {
      sentAs = decodeURIComponent(new URL(route.request().url()).pathname.split('/').pop()!);
      return route.fulfill({
        status: 202,
        contentType: 'application/json',
        body: JSON.stringify({ ...waiting, status: 'queued', stage: null, external: null }),
      });
    },
  );
  await page.reload();

  const panel = page.getByTestId('scan-external-wait');
  await expect(panel).toContainText('LiDAR odometry');
  await expect(page.getByTestId('survey-upload-status')).toHaveText('Waiting for a step done on another computer');
  // Read and inspected; the odometry and everything after it still to come.
  await expect(page.getByTestId('survey-upload-stage-inspect')).toBeVisible();

  const download = page.waitForEvent('download');
  await page.getByTestId('scan-external-bundle').click();
  expect((await download).suggestedFilename()).toBe('scan.hdmapping-odometry.zip');

  // The session comes back under whatever name the desktop gave it, and is sent under the step's.
  const session = test.info().outputPath('my refined run.zip');
  writeFileSync(session, Buffer.from('PK\u0005\u0006' + '\u0000'.repeat(18), 'binary'));
  await page.getByTestId('scan-external-send-session.zip').locator('xpath=ancestor::span[contains(@class,"ant-upload")]//input[@type="file"]').setInputFiles(session);
  await expect.poll(() => sentAs).toBe('session.zip');
});

test('a finished scan offers its session for refining, and the server refuses a refined session for a scan it made none for', async ({
  page,
  consoleErrors,
}) => {
  test.setTimeout(180_000);
  consoleErrors.allow(/status of 400/, 'the refined session is sent for a scan the server made no session for, and refused on purpose');
  const folder = test.info().outputPath('Refine_e2e');
  writeRecording(folder);
  const { uploadId } = await sendWithOdometryElsewhere(page, folder);

  await answerStatusWith(page, uploadId, (dto) => {
    dto.status = 'succeeded';
    dto.stage = 'store';
    (dto.pointCloud as Record<string, unknown>).sessionUrl = '/api/v1/files/invented-session';
  });
  await page.reload();

  const refine = page.getByTestId('scan-refine');
  await expect(refine).toBeVisible({ timeout: 30_000 });
  await expect(page.getByTestId('scan-refine-session')).toHaveAttribute('href', '/api/v1/files/invented-session');

  // The refined session goes to the real server as the next version of the scan's survey. This
  // recording never had a session made here, so the server refuses it, and says why.
  const session = test.info().outputPath('refined.zip');
  writeFileSync(session, Buffer.from('PK\u0005\u0006' + '\u0000'.repeat(18), 'binary'));
  const opening = page.waitForRequest(
    (request) => request.method() === 'POST' && new URL(request.url()).pathname === '/api/v1/survey-uploads',
  );
  await page.getByTestId('scan-refine-send').locator('xpath=ancestor::span[contains(@class,"ant-upload")]//input[@type="file"]').setInputFiles(session);
  const declared = (await opening).postDataJSON() as { caveSurvey: { id: string } | null; files: { name: string }[] };
  expect(declared.caveSurvey).not.toBeNull();
  expect(declared.files.map((file) => file.name)).toEqual(['session.zip']);
  await expect(page.getByText('A refined session is sent as the next version of a scan this server processed from a recording.')).toBeVisible();
});
